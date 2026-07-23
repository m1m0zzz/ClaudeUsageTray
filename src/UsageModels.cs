using System.Text.Json.Serialization;

namespace ClaudeUsageTray;

public sealed class UsageWindow
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public DateTimeOffset? ResetsAt { get; set; }

    // 実測ではいずれも null だったが、従量課金系のプランでは値が入る可能性がある
    [JsonPropertyName("limit_dollars")]
    public double? LimitDollars { get; set; }

    [JsonPropertyName("used_dollars")]
    public double? UsedDollars { get; set; }

    [JsonPropertyName("remaining_dollars")]
    public double? RemainingDollars { get; set; }
}

public sealed class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    /// <summary>最小通貨単位。decimal_places で割って表示する。</summary>
    [JsonPropertyName("monthly_limit")]
    public double? MonthlyLimit { get; set; }

    /// <summary>最小通貨単位。1503 / decimal_places=2 なら 15.03。</summary>
    [JsonPropertyName("used_credits")]
    public double? UsedCredits { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("decimal_places")]
    public int? DecimalPlaces { get; set; }

    [JsonPropertyName("spend_limit_reached")]
    public bool SpendLimitReached { get; set; }

    [JsonPropertyName("disabled_reason")]
    public string? DisabledReason { get; set; }

    /// <summary>
    /// 最小通貨単位の値を通貨表記にする。
    /// used_credits / monthly_limit はセント等の最小単位なので、そのまま出すと 100 倍ずれる。
    /// </summary>
    public string FormatAmount(double minorUnits)
    {
        int places = DecimalPlaces ?? 2;
        double value = minorUnits / Math.Pow(10, places);

        string symbol = Currency switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            "JPY" => "¥",
            _ => "",
        };

        string number = value.ToString("N" + places);

        return symbol.Length > 0
            ? symbol + number
            : $"{number} {Currency}".TrimEnd();
    }
}

/// <summary>
/// GET https://api.anthropic.com/api/oauth/usage のレスポンス。
/// プランによって seven_day 系は null で返ることがある。
/// </summary>
public sealed class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageWindow? SevenDay { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageWindow? SevenDaySonnet { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageWindow? SevenDayOpus { get; set; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; set; }
}

/// <summary>UI に渡す確定済みのスナップショット。</summary>
public sealed class UsageSnapshot
{
    public required DateTimeOffset FetchedAt { get; init; }
    public UsageResponse? Response { get; init; }

    /// <summary>取得に失敗した場合のユーザー向けメッセージ。成功時は null。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>再ログインが必要かどうか（＝ユーザーの操作が要る状態）。</summary>
    public bool RequiresLogin { get; init; }

    /// <summary>どの認証情報を使ったか。"Windows" / "WSL" / null。</summary>
    public string? SourceLabel { get; init; }

    /// <summary>各ソースの探索結果。ポップアップの診断表示に使う。</summary>
    public string? Diagnostic { get; init; }

    public bool IsError => ErrorMessage is not null;
}
