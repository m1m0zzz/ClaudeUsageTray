using System.Diagnostics;
using Microsoft.Win32;

namespace ClaudeUsageTray;

public sealed class TrayContext : ApplicationContext
{
    /// <summary>NotifyIcon.Text の上限。超えると例外になるため必ず切り詰める。</summary>
    private const int TooltipMaxLength = 63;

    /// <summary>手動更新の最短間隔。連打でレート制限を踏まないための保険。</summary>
    private static readonly TimeSpan ManualRefreshCooldown = TimeSpan.FromSeconds(60);

    private readonly AppConfig _config;
    private readonly UsageApiClient _client;
    private readonly ThresholdNotifier _notifier;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PopupForm _popup;
    private readonly ToolStripMenuItem _startupMenuItem;

    private readonly CancellationTokenSource _cts = new();

    private Icon? _currentIcon;
    private bool _isFetching;
    private DateTimeOffset _lastManualRefresh = DateTimeOffset.MinValue;

    /// <summary>直近の取得結果（成功・失敗を問わない）。</summary>
    private UsageSnapshot? _lastSnapshot;

    /// <summary>直近で成功した取得結果。取得失敗中の表示に使う。</summary>
    private UsageSnapshot? _lastGoodSnapshot;

    /// <summary>
    /// 次回の評価で通知を出さない。起動直後および閾値の再読み込み直後に立てる。
    /// 内部 state の記録は行うので、その後の閾値跨ぎは通常どおり通知される。
    /// </summary>
    private bool _suppressNextNotification = true;

    private sealed record Display(double? Percent, IconState State, string Tooltip);

    public TrayContext(AppConfig config)
    {
        _config = config;
        _client = new UsageApiClient(config);
        _notifier = new ThresholdNotifier(config);

        _popup = new PopupForm(config);
        _popup.RefreshRequested += (_, _) => RequestManualRefresh();
        _popup.SettingsRequested += (_, _) => OpenSettings();
        _popup.ExitRequested += (_, _) => ExitApplication();

        _startupMenuItem = new ToolStripMenuItem("Windows 起動時に開始")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled(),
        };
        _startupMenuItem.CheckedChanged += OnStartupToggled;

        var menu = new ContextMenuStrip();
        menu.Items.Add("今すぐ更新", null, (_, _) => RequestManualRefresh());
        menu.Items.Add("設定ファイルを開く", null, (_, _) => OpenSettings());
        menu.Items.Add("設定を再読み込み", null, (_, _) => ReloadSettings());
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = menu,
            Text = "Claude 使用状況（読み込み中）",
        };
        _notifyIcon.MouseClick += OnTrayClick;

        ApplyIcon(null, IconState.Unknown);

        _timer = new System.Windows.Forms.Timer
        {
            Interval = _config.PollSeconds * 1000,
        };
        _timer.Tick += async (_, _) => await FetchAsync().ConfigureAwait(true);
        _timer.Start();

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // 起動直後に一度取得する
        _ = FetchAsync();
    }

    // ---- 取得 ----

    private async Task FetchAsync()
    {
        if (_isFetching) return;
        _isFetching = true;

        try
        {
            UsageSnapshot snapshot = await _client.FetchAsync(_cts.Token).ConfigureAwait(true);
            _lastSnapshot = snapshot;

            if (!snapshot.IsError && snapshot.Response is { } response)
            {
                _lastGoodSnapshot = snapshot;

                // トーストは新鮮なデータでのみ評価する。古い値で再発火させない。
                //
                // 重要: 起動直後でも Evaluate は必ず呼ぶ。
                // 呼ばずに飛ばすと内部の level が 0 のままになり、
                // 次回のポーリングで結局発火してしまう。
                IReadOnlyList<ToastMessage> toasts = _notifier.Evaluate(response);

                if (_suppressNextNotification)
                {
                    // 起動時点で既に閾値を超えていた場合、それはアイコンの色で分かる。
                    // 「見ていなかった間に跨いだ」ぶんは通知しない。
                    _suppressNextNotification = false;
                }
                else
                {
                    foreach (ToastMessage toast in toasts)
                    {
                        ShowToast(toast);
                    }
                }
            }

            Refresh();
        }
        catch (OperationCanceledException)
        {
            // 終了中
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"取得中に予期しない例外: {ex}");

            _lastSnapshot = new UsageSnapshot
            {
                FetchedAt = DateTimeOffset.Now,
                ErrorMessage = "予期しないエラーが発生しました。",
            };

            Refresh();
        }
        finally
        {
            _isFetching = false;
        }
    }

    private void RequestManualRefresh()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        if (now - _lastManualRefresh < ManualRefreshCooldown)
        {
            int wait = (int)(ManualRefreshCooldown - (now - _lastManualRefresh)).TotalSeconds;
            ShowToast(new ToastMessage(
                "更新の間隔が短すぎます",
                $"サーバー側のレート制限を避けるため、あと {wait} 秒待ってください。"));
            return;
        }

        _lastManualRefresh = now;
        _ = FetchAsync();
    }

    // ---- 表示の決定 ----

    /// <summary>
    /// アイコンとツールチップを現在の状態から決める。
    /// 取得完了時とテーマ/DPI 変更時の両方から呼ばれるため、判断は必ずここに集約する。
    /// </summary>
    private Display ComputeDisplay()
    {
        UsageSnapshot? current = _lastSnapshot;

        if (current is null)
        {
            return new Display(null, IconState.Unknown, "Claude 使用状況（読み込み中）");
        }

        // 成功
        if (!current.IsError && current.Response is { } response)
        {
            double? percent = _config.ToPercent(response.FiveHour?.Utilization);

            IconState state = percent is null ? IconState.Unknown
                : percent.Value >= 100 ? IconState.Full
                : IconState.Normal;

            return new Display(percent, state, BuildTooltip(response));
        }

        // 失敗かつユーザーの操作が必要（再ログイン等）
        if (current.RequiresLogin)
        {
            return new Display(
                null, IconState.Error, current.ErrorMessage ?? "再ログインが必要です。");
        }

        // 失敗だが直近の成功値がある → グレーで表示し続ける
        if (_lastGoodSnapshot?.Response is { } lastResponse)
        {
            TimeSpan age = DateTimeOffset.Now - _lastGoodSnapshot.FetchedAt;

            if (age <= TimeSpan.FromMinutes(_config.MaxStaleMinutes))
            {
                double? percent = _config.ToPercent(lastResponse.FiveHour?.Utilization);

                return new Display(
                    percent,
                    IconState.Stale,
                    $"{BuildTooltip(lastResponse)}\n{FormatAge(age)}前の値");
            }
        }

        return new Display(
            null, IconState.Unknown, current.ErrorMessage ?? "取得できていません。");
    }

    private void Refresh()
    {
        Display display = ComputeDisplay();

        ApplyIcon(display.Percent, display.State);
        SetTooltip(display.Tooltip);

        if (_popup.Visible && _lastSnapshot is not null)
        {
            _popup.UpdateData(_lastSnapshot, _lastGoodSnapshot);
        }
    }

    private void ApplyIcon(double? percent, IconState state)
    {
        Icon newIcon = IconRenderer.Render(percent, state, ThemeHelper.IsTaskbarDark());

        Icon? old = _currentIcon;
        _currentIcon = newIcon;
        _notifyIcon.Icon = newIcon;

        old?.Dispose();
    }

    private void SetTooltip(string text) => _notifyIcon.Text = Truncate(text);

    private static string Truncate(string text) =>
        text.Length <= TooltipMaxLength
            ? text
            : text[..(TooltipMaxLength - 1)] + "…";

    private string BuildTooltip(UsageResponse response)
    {
        string five = FormatShort(_config.ToPercent(response.FiveHour?.Utilization));
        string week = FormatShort(_config.ToPercent(response.SevenDay?.Utilization));

        string line1 = response.SevenDay is null
            ? $"5h {five}"
            : $"5h {five} / 週 {week}";

        string line2 = FormatResetShort("5h", response.FiveHour?.ResetsAt);
        string line3 = response.SevenDay is null
            ? ""
            : FormatResetShort("週", response.SevenDay.ResetsAt);

        return string.Join('\n',
            new[] { line1, line2, line3 }.Where(s => s.Length > 0));
    }

    internal static string FormatAge(TimeSpan age) =>
        age.TotalDays >= 1 ? $"{(int)age.TotalDays}日"
        : age.TotalHours >= 1 ? $"{(int)age.TotalHours}時間"
        : $"{Math.Max((int)age.TotalMinutes, 1)}分";

    private static string FormatShort(double? percent) =>
        percent is null ? "—" : $"{percent.Value:0}%";

    private static string FormatResetShort(string label, DateTimeOffset? resetsAt) =>
        resetsAt is null ? "" : $"{label}回復 {resetsAt.Value.ToLocalTime():M/d HH:mm}";

    private void ShowToast(ToastMessage toast)
    {
        // ShowBalloonTip は Windows 10/11 では通常のトーストとして表示される。
        // 集中モード（応答不可）が有効なときは抑制される点に注意。
        _notifyIcon.BalloonTipTitle = toast.Title;
        _notifyIcon.BalloonTipText = toast.Body;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(10_000);
    }

    // ---- 操作 ----

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        if (_popup.Visible)
        {
            _popup.Hide();
            return;
        }

        UsageSnapshot snapshot = _lastSnapshot ?? new UsageSnapshot
        {
            FetchedAt = DateTimeOffset.Now,
            ErrorMessage = "まだ取得していません。",
        };

        _popup.UpdateData(snapshot, _lastGoodSnapshot);
        _popup.ShowNearTray();
    }

    private void OnStartupToggled(object? sender, EventArgs e)
    {
        try
        {
            StartupManager.Apply(_startupMenuItem.Checked);
            _config.RunAtStartup = _startupMenuItem.Checked;
            _config.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"自動起動の設定を変更できませんでした。\n{ex.Message}",
                "ClaudeUsageTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            _startupMenuItem.Checked = StartupManager.IsEnabled();
        }
    }

    private void OpenSettings()
    {
        try
        {
            if (!File.Exists(AppConfig.ConfigPath)) _config.Save();

            Process.Start(new ProcessStartInfo
            {
                FileName = AppConfig.ConfigPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"設定ファイルを開けませんでした。\n{AppConfig.ConfigPath}\n{ex.Message}",
                "ClaudeUsageTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ReloadSettings()
    {
        try
        {
            AppConfig loaded = AppConfig.Load();
            _config.CopyFrom(loaded);
            _popup.SyncSettings();

            _timer.Interval = _config.PollSeconds * 1000;

            // Reset で level が消えるため、直後の評価は起動直後と同じ扱いにする。
            // これをしないと、閾値超過中に設定を編集しただけでトーストが飛ぶ。
            _notifier.Reset();
            _suppressNextNotification = true;

            ShowToast(new ToastMessage(
                "設定を再読み込みしました", $"更新間隔 {_config.PollSeconds} 秒"));

            _ = FetchAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"設定を読み込めませんでした。書式を確認してください。\n{ex.Message}",
                "ClaudeUsageTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    // ---- システムイベント ----

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            Refresh();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Refresh();

    // ---- 後始末 ----

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            _cts.Cancel();
            _timer.Stop();
            _timer.Dispose();

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            _currentIcon?.Dispose();
            _popup.Dispose();
            _client.Dispose();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }
}
