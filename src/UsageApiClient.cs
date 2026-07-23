using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>
/// Anthropic の非公開エンドポイント /api/oauth/usage を叩く。
/// 公式 API ではないため、予告なく仕様変更・廃止される可能性がある。
/// </summary>
public sealed class UsageApiClient : IDisposable
{
    private const string Endpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeaderValue = "oauth-2025-04-20";

    private readonly HttpClient _http;
    private readonly AppConfig _config;

    public UsageApiClient(AppConfig config)
    {
        _config = config;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeUsageTray/0.1");
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;

        CredentialResolution resolution = CredentialStore.Resolve(_config);

        if (resolution.IsEmpty)
        {
            // Windows 側だけ見て諦めていた場合、WSL 側も強制的に探しておく
            if (!resolution.WslExamined)
            {
                resolution = CredentialStore.Resolve(_config, forceAll: true);
            }

            if (resolution.IsEmpty)
            {
                bool actionableEmpty = !resolution.WslUnavailable;

                return new UsageSnapshot
                {
                    FetchedAt = now,
                    ErrorMessage = actionableEmpty
                        ? "認証情報が見つかりません。Claude Code で /login を実行してください。"
                        : "認証情報を読めませんでした。WSL が停止している可能性があります。",
                    RequiresLogin = actionableEmpty,
                    Diagnostic = resolution.DiagnosticText,
                };
            }
        }

        var tried = new HashSet<string>();
        UsageSnapshot? lastFailure = null;

        foreach (Credential credential in resolution.Candidates)
        {
            if (!tried.Add(credential.AccessToken)) continue;

            UsageSnapshot attempt = await TryFetchAsync(credential, now, ct).ConfigureAwait(false);

            if (!attempt.IsError) return attempt;

            lastFailure = attempt;

            // 401 以外（通信断・レート制限・サーバーエラー）は
            // 別の認証情報で試しても意味がないので即座に返す。
            if (!attempt.RequiresLogin) return attempt;
        }

        // ここまで来た＝手持ちの候補すべてで 401。
        // Windows 側しか見ていなかったなら WSL 側も試す（expiresAt の解釈ミスに対する保険）。
        if (!resolution.WslExamined)
        {
            CredentialResolution all = CredentialStore.Resolve(_config, forceAll: true);

            foreach (Credential credential in all.Candidates)
            {
                if (!tried.Add(credential.AccessToken)) continue;

                UsageSnapshot attempt =
                    await TryFetchAsync(credential, now, ct).ConfigureAwait(false);

                if (!attempt.IsError) return attempt;

                lastFailure = attempt;
                if (!attempt.RequiresLogin) return attempt;
            }

            resolution = all;
        }

        // ここまで来た＝手持ちの候補すべてで 401。
        // WSL を確認できなかった（停止中など）場合、これは「再ログインが必要」ではなく
        // 「現役の認証情報が読めなかった」なので、ユーザーに操作を促さない。
        bool actionable = !resolution.WslUnavailable;

        return new UsageSnapshot
        {
            FetchedAt = now,
            ErrorMessage = actionable
                ? (lastFailure?.ErrorMessage
                   ?? "認証に失敗しました。Claude Code で /login を実行してください。")
                : "現役の認証情報を読めませんでした。WSL が停止している可能性があります。",
            RequiresLogin = actionable,
            Diagnostic = resolution.DiagnosticText,
        };
    }

    private async Task<UsageSnapshot> TryFetchAsync(
        Credential credential, DateTimeOffset now, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        request.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeaderValue);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure(now, "タイムアウトしました。ネットワークを確認してください。", credential);
        }
        catch (HttpRequestException ex)
        {
            return Failure(now, $"通信に失敗しました: {ex.Message}", credential);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new UsageSnapshot
                {
                    FetchedAt = now,
                    ErrorMessage =
                        $"{credential.SourceLabel} のトークンが無効です。Claude Code で /login を実行してください。",
                    RequiresLogin = true,
                    SourceLabel = credential.SourceLabel,
                };
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(now, "レート制限中です。次回の更新まで待ちます。", credential);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(now, $"サーバーが {(int)response.StatusCode} を返しました。", credential);
            }

            if (_config.LogRawResponse) TryWriteRawLog(body);

            try
            {
                var parsed = JsonSerializer.Deserialize<UsageResponse>(body);
                if (parsed is null)
                {
                    return Failure(now, "レスポンスを解釈できませんでした。", credential);
                }

                return new UsageSnapshot
                {
                    FetchedAt = now,
                    Response = parsed,
                    SourceLabel = credential.SourceLabel,
                };
            }
            catch (JsonException ex)
            {
                return Failure(now, $"レスポンスの形式が想定と違います: {ex.Message}", credential);
            }
        }
    }

    private static UsageSnapshot Failure(
        DateTimeOffset at, string message, Credential credential) =>
        new()
        {
            FetchedAt = at,
            ErrorMessage = message,
            SourceLabel = credential.SourceLabel,
        };

    private static void TryWriteRawLog(string body)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.DataDirectory);
            File.WriteAllText(AppConfig.RawLogPath, body);
        }
        catch
        {
            // 診断用の副作用なので失敗は無視する
        }
    }

    public void Dispose() => _http.Dispose();
}
