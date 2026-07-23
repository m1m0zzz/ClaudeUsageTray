namespace ClaudeUsageTray;

public sealed record ToastMessage(string Title, string Body);

/// <summary>
/// 閾値超過の通知を「跨いだ瞬間に 1 回だけ」出す。
///
/// 3 分間隔でポーリングするため、単純な比較だと 80% を超えた状態が続く間ずっと
/// 通知が飛び続ける。そのためレベル（0=通常 / 1=警告 / 2=上限）を記憶し、
/// レベルが上がったときだけ発火する。
/// resets_at が変わった＝枠がリセットされたとみなしてレベルを 0 に戻す（再武装）。
/// </summary>
public sealed class ThresholdNotifier
{
    private sealed class QuotaState
    {
        public int Level;
        public DateTimeOffset? ArmedFor;
    }

    private readonly AppConfig _config;
    private readonly Dictionary<string, QuotaState> _states = new();

    public ThresholdNotifier(AppConfig config) => _config = config;

    public IReadOnlyList<ToastMessage> Evaluate(UsageResponse response)
    {
        var messages = new List<ToastMessage>();

        if (_config.NotifyFiveHour)
        {
            Check(messages, "five_hour", "5時間枠", response.FiveHour);
        }

        if (_config.NotifySevenDay)
        {
            Check(messages, "seven_day", "週間枠", response.SevenDay);
        }

        return messages;
    }

    /// <summary>取得失敗が続いた後に誤発火しないよう、明示的にリセットしたい場合に使う。</summary>
    public void Reset() => _states.Clear();

    private void Check(
        List<ToastMessage> messages, string key, string label, UsageWindow? window)
    {
        if (window is null) return;

        double? percent = _config.ToPercent(window.Utilization);
        if (percent is null) return;

        if (!_states.TryGetValue(key, out QuotaState? state))
        {
            state = new QuotaState();
            _states[key] = state;
        }

        // 枠がリセットされたら再武装する
        if (state.ArmedFor != window.ResetsAt)
        {
            state.ArmedFor = window.ResetsAt;
            state.Level = 0;
        }

        int level = percent.Value >= _config.LimitThresholdPercent ? 2
            : percent.Value >= _config.WarnThresholdPercent ? 1
            : 0;

        if (level <= state.Level)
        {
            // 下がった場合は静かに追従するだけ（再度上がれば発火する）
            state.Level = level;
            return;
        }

        // 80% を飛ばして 100% に到達した場合、通知は上限のぶん 1 回だけ出す
        state.Level = level;

        string resetText = FormatReset(window.ResetsAt);

        if (level == 2)
        {
            messages.Add(new ToastMessage(
                $"{label}を使い切りました",
                $"使用率 {percent.Value:0}%。{resetText}"));
        }
        else
        {
            messages.Add(new ToastMessage(
                $"{label}が {_config.WarnThresholdPercent:0}% を超えました",
                $"使用率 {percent.Value:0}%。{resetText}"));
        }
    }

    internal static string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null) return "回復時刻は不明です。";

        DateTimeOffset local = resetsAt.Value.ToLocalTime();
        TimeSpan remaining = local - DateTimeOffset.Now;

        if (remaining <= TimeSpan.Zero) return "まもなく回復します。";

        string span = remaining.TotalDays >= 1
            ? $"{(int)remaining.TotalDays}日{remaining.Hours}時間後"
            : remaining.TotalHours >= 1
                ? $"{(int)remaining.TotalHours}時間{remaining.Minutes}分後"
                : $"{remaining.Minutes}分後";

        return $"{local:M/d HH:mm}（{span}）に回復します。";
    }
}
