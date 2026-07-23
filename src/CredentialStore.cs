using System.Text.Json;

namespace ClaudeUsageTray;

public enum CredentialSource
{
    Windows,
    Wsl,
}

public sealed record Credential(
    string AccessToken,
    DateTimeOffset? ExpiresAt,
    CredentialSource Source,
    string Origin)
{
    /// <summary>時計ずれと有効期限直前の取りこぼしを避けるための余裕。</summary>
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// expiresAt が読めなかった場合は「有効かもしれない」として扱う。
    /// 最終的な判定は API の 401 に委ねる。
    /// </summary>
    public bool LooksValid =>
        ExpiresAt is null || ExpiresAt.Value > DateTimeOffset.Now + Skew;

    public string SourceLabel => Source == CredentialSource.Windows ? "Windows" : "WSL";
}

public sealed record CredentialResolution(
    IReadOnlyList<Credential> Candidates,
    IReadOnlyList<string> Diagnostics,
    bool WslExamined,
    bool WslUnavailable)
{
    public bool IsEmpty => Candidates.Count == 0;

    public string DiagnosticText => Diagnostics.Count == 0
        ? "認証情報が見つかりませんでした。"
        : string.Join(" / ", Diagnostics);
}

/// <summary>
/// Claude Code CLI が保存した OAuth トークンを Windows / WSL の両方から探す。
///
/// 選択基準は「ファイルの存在」ではなく「expiresAt が有効かどうか」。
/// Windows 側にファイルが残っていても期限切れなら WSL 側へ回す。
/// 書き込みは一切行わない（自前リフレッシュは Claude Code 本体のログインを壊す恐れがある）。
/// </summary>
public static class CredentialStore
{
    private const string LinuxPath = "~/.claude/.credentials.json";

    public static string WindowsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            ".credentials.json");

    /// <summary>
    /// 候補を優先順に返す。
    /// </summary>
    /// <param name="forceAll">
    /// true にすると Windows 側が有効に見えても WSL 側を読む。
    /// 401 を食らった後の再試行用。
    /// </param>
    public static CredentialResolution Resolve(AppConfig config, bool forceAll = false)
    {
        var candidates = new List<Credential>();
        var diagnostics = new List<string>();

        // ---- Windows 側 ----
        Credential? windows = TryReadWindows(config, diagnostics);
        if (windows is not null) candidates.Add(windows);

        bool windowsUsable = windows?.LooksValid == true;

        // Windows 側が有効なら WSL には触らない。
        // wsl.exe の起動コスト、および停止中ディストロを起こしてしまう副作用を避けるため。
        bool shouldTryWsl = config.UseWslFallback && (forceAll || !windowsUsable);

        if (!shouldTryWsl)
        {
            return new CredentialResolution(
                Order(candidates), diagnostics, WslExamined: false, WslUnavailable: false);
        }

        Credential? wsl = TryReadWsl(config, diagnostics, out bool wslUnavailable);
        if (wsl is not null) candidates.Add(wsl);

        return new CredentialResolution(
            Order(candidates), diagnostics, WslExamined: true, wslUnavailable);
    }

    /// <summary>有効そうなものを先に、同条件なら Windows を先に、次に期限が新しい順。</summary>
    private static IReadOnlyList<Credential> Order(List<Credential> candidates) =>
        candidates
            .OrderByDescending(c => c.LooksValid)
            .ThenBy(c => c.Source == CredentialSource.Windows ? 0 : 1)
            .ThenByDescending(c => c.ExpiresAt ?? DateTimeOffset.MinValue)
            .ToList();

    private static Credential? TryReadWindows(AppConfig config, List<string> diagnostics)
    {
        string path = string.IsNullOrWhiteSpace(config.CredentialsPathOverride)
            ? WindowsPath
            : config.CredentialsPathOverride;

        if (!File.Exists(path))
        {
            diagnostics.Add("Windows: ファイルなし");
            return null;
        }

        string json;
        try
        {
            // Claude Code が書き込み中でも読めるよう共有指定で開く
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Windows: 読み取り失敗 ({ex.GetType().Name})");
            return null;
        }

        Credential? credential = Parse(json, CredentialSource.Windows, path);

        if (credential is null)
        {
            diagnostics.Add("Windows: accessToken なし");
        }
        else if (!credential.LooksValid)
        {
            diagnostics.Add($"Windows: 期限切れ ({credential.ExpiresAt:M/d HH:mm})");
        }

        return credential;
    }

    private static Credential? TryReadWsl(
        AppConfig config, List<string> diagnostics, out bool unavailable)
    {
        unavailable = false;

        if (!WslBridge.IsAvailable)
        {
            diagnostics.Add("WSL: 未インストール");
            unavailable = true;
            return null;
        }

        // 停止中のディストロを起こさないよう、起動中かを先に確認する
        if (!config.AllowWslAutoStart && !WslBridge.IsAnyDistroRunning(config.WslDistro))
        {
            diagnostics.Add("WSL: 停止中のため読み取りをスキップ");
            unavailable = true;
            return null;
        }

        WslReadResult result = WslBridge.ReadFile(config.WslDistro, LinuxPath);

        if (!result.Success)
        {
            diagnostics.Add($"WSL: {result.Diagnostic}");
            unavailable = true;
            return null;
        }

        string origin = string.IsNullOrWhiteSpace(config.WslDistro)
            ? $"WSL:{LinuxPath}"
            : $"WSL[{config.WslDistro}]:{LinuxPath}";

        Credential? credential = Parse(result.Content, CredentialSource.Wsl, origin);

        if (credential is null)
        {
            diagnostics.Add("WSL: accessToken なし");
        }
        else if (!credential.LooksValid)
        {
            diagnostics.Add($"WSL: 期限切れ ({credential.ExpiresAt:M/d HH:mm})");
        }

        return credential;
    }

    private static Credential? Parse(string json, CredentialSource source, string origin)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // claudeAiOauth 配下が標準。形式変更に備えてルート直下も見る。
            JsonElement scope = root.TryGetProperty("claudeAiOauth", out var oauth) ? oauth : root;

            if (!scope.TryGetProperty("accessToken", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string token = tokenElement.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(token)) return null;

            return new Credential(token, ParseExpiry(scope), source, origin);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseExpiry(JsonElement scope)
    {
        if (!scope.TryGetProperty("expiresAt", out var element)) return null;

        try
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out long ms))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }

            if (element.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(element.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
            // 形式が想定外でも致命傷にしない。期限切れは API の 401 で検出する。
        }

        return null;
    }
}
