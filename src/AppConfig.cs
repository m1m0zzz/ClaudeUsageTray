using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsageTray;

/// <summary>utilization の解釈。API のレスポンス実測で確定させる。</summary>
public enum UtilizationScale
{
    /// <summary>0〜100 の百分率としてそのまま扱う（既定）。</summary>
    Percent,

    /// <summary>0〜1 の小数として扱い、100 倍する。</summary>
    Fraction,
}

public sealed class AppConfig
{
    /// <summary>
    /// ポーリング間隔（秒）。/api/oauth/usage は短間隔だと 429 が張り付くため 180 秒未満は許可しない。
    /// </summary>
    [JsonPropertyName("pollSeconds")]
    public int PollSeconds { get; set; } = 180;

    [JsonPropertyName("warnThresholdPercent")]
    public double WarnThresholdPercent { get; set; } = 80;

    [JsonPropertyName("limitThresholdPercent")]
    public double LimitThresholdPercent { get; set; } = 100;

    [JsonPropertyName("notifyFiveHour")]
    public bool NotifyFiveHour { get; set; } = true;

    [JsonPropertyName("notifySevenDay")]
    public bool NotifySevenDay { get; set; } = true;

    [JsonPropertyName("runAtStartup")]
    public bool RunAtStartup { get; set; } = true;

    [JsonPropertyName("utilizationScale")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UtilizationScale UtilizationScale { get; set; } = UtilizationScale.Percent;

    /// <summary>診断用。生のレスポンス JSON を %APPDATA% 配下に書き出す。</summary>
    [JsonPropertyName("logRawResponse")]
    public bool LogRawResponse { get; set; } = false;

    /// <summary>credentials.json の場所を明示したい場合に指定。空なら既定パス。</summary>
    [JsonPropertyName("credentialsPathOverride")]
    public string CredentialsPathOverride { get; set; } = "";

    /// <summary>Windows 側のトークンが無効なとき、WSL 側の credentials.json を読む。</summary>
    [JsonPropertyName("useWslFallback")]
    public bool UseWslFallback { get; set; } = true;

    /// <summary>読みに行くディストロ名。空なら既定のディストロ。</summary>
    [JsonPropertyName("wslDistro")]
    public string WslDistro { get; set; } = "";

    /// <summary>
    /// true にすると、停止中の WSL ディストロを起動してでも読みに行く。
    /// 3 分おきに WSL を起こし続けることになるため既定は false。
    /// </summary>
    [JsonPropertyName("allowWslAutoStart")]
    public bool AllowWslAutoStart { get; set; } = false;

    /// <summary>
    /// 取得に失敗したとき、直近の成功値をグレー表示で使い続ける上限（分）。
    /// これを超えたら数値ではなく「?」を出す。
    /// </summary>
    [JsonPropertyName("maxStaleMinutes")]
    public int MaxStaleMinutes { get; set; } = 720;

    // ---- パス ----

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageTray");

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");

    public static string RawLogPath => Path.Combine(DataDirectory, "last-response.json");

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Load()
    {
        Directory.CreateDirectory(DataDirectory);

        if (!File.Exists(ConfigPath))
        {
            var fresh = new AppConfig();
            fresh.Save();
            return fresh;
        }

        string json = File.ReadAllText(ConfigPath);
        var config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions) ?? new AppConfig();
        config.Normalize();
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, WriteOptions));
    }

    /// <summary>
    /// 再読み込み用。インスタンスを差し替えず値だけ上書きする。
    /// UsageApiClient / ThresholdNotifier / PopupForm が同じインスタンスを
    /// 参照しているため、置き換えると反映されない。
    /// </summary>
    public void CopyFrom(AppConfig other)
    {
        PollSeconds = other.PollSeconds;
        WarnThresholdPercent = other.WarnThresholdPercent;
        LimitThresholdPercent = other.LimitThresholdPercent;
        NotifyFiveHour = other.NotifyFiveHour;
        NotifySevenDay = other.NotifySevenDay;
        RunAtStartup = other.RunAtStartup;
        UtilizationScale = other.UtilizationScale;
        LogRawResponse = other.LogRawResponse;
        CredentialsPathOverride = other.CredentialsPathOverride;
        UseWslFallback = other.UseWslFallback;
        WslDistro = other.WslDistro;
        AllowWslAutoStart = other.AllowWslAutoStart;
        MaxStaleMinutes = other.MaxStaleMinutes;

        Normalize();
    }

    private void Normalize()
    {
        // 180 秒未満はレート制限を踏むので強制的に引き上げる
        if (PollSeconds < 180) PollSeconds = 180;
        if (PollSeconds > 3600) PollSeconds = 3600;

        WarnThresholdPercent = Math.Clamp(WarnThresholdPercent, 1, 100);
        LimitThresholdPercent = Math.Clamp(LimitThresholdPercent, 1, 100);

        MaxStaleMinutes = Math.Clamp(MaxStaleMinutes, 5, 10080);

        if (WarnThresholdPercent > LimitThresholdPercent)
        {
            (WarnThresholdPercent, LimitThresholdPercent) =
                (LimitThresholdPercent, WarnThresholdPercent);
        }
    }

    /// <summary>API の生値を 0〜100 の百分率に揃える。</summary>
    public double? ToPercent(double? raw)
    {
        if (raw is null) return null;

        double value = UtilizationScale == UtilizationScale.Fraction
            ? raw.Value * 100d
            : raw.Value;

        if (double.IsNaN(value) || double.IsInfinity(value)) return null;

        return Math.Clamp(value, 0d, 100d);
    }
}
