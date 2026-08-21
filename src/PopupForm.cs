using System.Drawing.Drawing2D;

namespace ClaudeUsageTray;

/// <summary>
/// トレイアイコン左クリックで出す詳細パネル。
/// 枠線なしフォームを自前描画し、フォーカスが外れたら閉じる。
/// </summary>
public sealed class PopupForm : Form
{
    private sealed record Row(string Label, double? Percent, string Sub, bool Muted = false);

    /// <summary>背景・枠線・フォーカスを同じ丸角パスで描画するボタン。</summary>
    private sealed class RoundedButton : Button
    {
        private bool _hovered;
        private bool _pressed;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = BackColor;
            if (_pressed && !FlatAppearance.MouseDownBackColor.IsEmpty)
            {
                fill = FlatAppearance.MouseDownBackColor;
            }
            else if (_hovered && !FlatAppearance.MouseOverBackColor.IsEmpty)
            {
                fill = FlatAppearance.MouseOverBackColor;
            }

            using GraphicsPath surface = PopupForm.RoundedRect(
                new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
                PopupForm.ButtonCornerRadius);
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, surface);
            }

            using (var pen = new Pen(FlatAppearance.BorderColor))
            {
                g.DrawPath(pen, surface);
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                ClientRectangle,
                Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
            {
                using GraphicsPath focus = PopupForm.RoundedRect(
                    new RectangleF(3.5f, 3.5f, Width - 7, Height - 7),
                    3.5f);
                using var focusPen = new Pen(ForeColor) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, focus);
            }
        }
    }

    /// <summary>埋め込み SVG でベル／ミュート状態を描画するトグル。</summary>
    private sealed class NotificationToggleButton : CheckBox
    {
        private bool _hovered;
        private readonly SvgPathIcon _bellIcon;
        private readonly SvgPathIcon _bellOffIcon;
        private Color _iconColor = SystemColors.ControlText;
        private Color _borderColor = SystemColors.ControlDark;
        private Color _hoverBackColor = SystemColors.ControlLight;
        private Color _checkedBackColor = SystemColors.ControlLight;

        public NotificationToggleButton()
        {
            _bellIcon = SvgPathIcon.Load("ClaudeUsageTray.Assets.bell.svg");
            _bellOffIcon = SvgPathIcon.Load("ClaudeUsageTray.Assets.bell-off.svg");
            Appearance = Appearance.Button;
            AutoSize = false;
            Size = new Size(32, 28);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;

            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bellIcon.Dispose();
                _bellOffIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        public void ApplyColors(
            Color backColor,
            Color iconColor,
            Color borderColor,
            Color hoverBackColor,
            Color checkedBackColor)
        {
            BackColor = backColor;
            _iconColor = iconColor;
            _borderColor = borderColor;
            _hoverBackColor = hoverBackColor;
            _checkedBackColor = checkedBackColor;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill = Checked
                ? _checkedBackColor
                : _hovered ? _hoverBackColor : BackColor;
            using var surface = PopupForm.RoundedRect(
                new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
                PopupForm.ButtonCornerRadius);
            using (var fillBrush = new SolidBrush(fill))
            {
                g.FillPath(fillBrush, surface);
            }

            using (var borderPen = new Pen(_borderColor))
            {
                g.DrawPath(borderPen, surface);
            }

            float cx = Width / 2f;
            SvgPathIcon icon = Checked ? _bellIcon : _bellOffIcon;
            Color iconColor = !Enabled
                ? SystemColors.GrayText
                : Checked ? _iconColor : Color.FromArgb(0xD9, 0x30, 0x25);
            icon.Draw(
                g,
                iconColor,
                new RectangleF(cx - 9, 4, 18, 18),
                1.7f);

            if (Focused && ShowFocusCues)
            {
                using var focus = PopupForm.RoundedRect(
                    new RectangleF(3.5f, 3.5f, Width - 7, Height - 7),
                    3.5f);
                using var focusPen = new Pen(_iconColor) { DashStyle = DashStyle.Dot };
                g.DrawPath(focusPen, focus);
            }
        }
    }

    private const int PanelWidth = 320;
    private const int PaddingX = 16;
    private const int HeaderHeight = 44;
    private const int RowHeight = 52;
    private const int FooterHeight = 74;
    private const float PanelCornerRadius = 12f;
    private const float ButtonCornerRadius = 6f;

    private readonly AppConfig _config;
    private readonly List<Row> _rows = new();

    private readonly NotificationToggleButton _notificationsToggle;
    private readonly ToolTip _notificationToolTip;
    private readonly Button _refreshButton;
    private readonly Button _settingsButton;
    private readonly Button _exitButton;

    private string _statusText = "読み込み中…";
    private string _diagnostic = "";
    private bool _isStale;
    private bool _dark;
    private bool _syncingNotificationSetting;

    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public PopupForm(AppConfig config)
    {
        _config = config;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        KeyPreview = true;
        Width = PanelWidth;
        Height = HeaderHeight + FooterHeight;

        _notificationsToggle = new NotificationToggleButton
        {
            Checked = _config.NotificationsEnabled,
            Location = new Point(PanelWidth - PaddingX - 32, 8),
        };
        _notificationToolTip = new ToolTip();

        _refreshButton = MakeButton("更新");
        _settingsButton = MakeButton("設定");
        _exitButton = MakeButton("終了");

        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        _settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        _notificationsToggle.CheckedChanged += OnNotificationsToggled;

        Controls.Add(_notificationsToggle);
        Controls.Add(_refreshButton);
        Controls.Add(_settingsButton);
        Controls.Add(_exitButton);

        ApplyTheme();
        UpdateRoundedRegions();
        UpdateNotificationTogglePresentation();
    }

    /// <summary>設定ファイルの再読み込み後に UI を同期する。</summary>
    public void SyncSettings()
    {
        _syncingNotificationSetting = true;
        try
        {
            _notificationsToggle.Checked = _config.NotificationsEnabled;
        }
        finally
        {
            _syncingNotificationSetting = false;
        }
    }

    private void OnNotificationsToggled(object? sender, EventArgs e)
    {
        UpdateNotificationTogglePresentation();
        if (_syncingNotificationSetting) return;

        bool previous = _config.NotificationsEnabled;
        _config.NotificationsEnabled = _notificationsToggle.Checked;

        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            _config.NotificationsEnabled = previous;
            SyncSettings();

            MessageBox.Show(
                $"通知設定を保存できませんでした。\n{ex.Message}",
                "ClaudeUsageTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void UpdateNotificationTogglePresentation()
    {
        string state = _notificationsToggle.Checked ? "ON" : "OFF";
        string action = _notificationsToggle.Checked ? "OFF" : "ON";

        _notificationsToggle.AccessibleName = $"使用率通知: {state}";
        _notificationsToggle.AccessibleDescription =
            $"使用率の閾値通知は {state} です。クリックして {action} にします。";
        _notificationToolTip.SetToolTip(
            _notificationsToggle,
            $"使用率通知: {state}（クリックで {action}）");
    }

    protected override bool ShowWithoutActivation => false;

    private static Button MakeButton(string text) => new RoundedButton
    {
        Text = text,
        Height = 28,
        Width = 88,
        TabStop = false,
        Cursor = Cursors.Hand,
        Font = new Font("Yu Gothic UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) _notificationToolTip.Dispose();
        base.Dispose(disposing);
    }

    // ---- データ更新 ----

    public void UpdateData(UsageSnapshot snapshot, UsageSnapshot? lastGood = null)
    {
        _rows.Clear();
        _diagnostic = snapshot.Diagnostic ?? "";

        UsageResponse? source;

        if (!snapshot.IsError && snapshot.Response is { } fresh)
        {
            source = fresh;
            _statusText = $"最終更新 {snapshot.FetchedAt.ToLocalTime():HH:mm:ss}"
                + (snapshot.SourceLabel is null ? "" : $"  ·  {snapshot.SourceLabel}");
            _isStale = false;
        }
        else if (!snapshot.RequiresLogin && lastGood?.Response is { } previous)
        {
            // 取得に失敗しても、直近の成功値があるなら空パネルより有用
            source = previous;
            TimeSpan age = DateTimeOffset.Now - lastGood.FetchedAt;
            _statusText = $"{TrayContext.FormatAge(age)}前の値  ·  {snapshot.ErrorMessage}";
            _isStale = true;
        }
        else
        {
            _statusText = snapshot.ErrorMessage ?? "取得できていません。";
            _isStale = false;
            RelayoutAndRedraw();
            return;
        }

        AddWindowRow("5時間枠", source.FiveHour);
        AddWindowRow("週間枠（全体）", source.SevenDay);
        AddWindowRow("週間枠（Sonnet）", source.SevenDaySonnet);
        AddWindowRow("週間枠（Opus）", source.SevenDayOpus);
        AddExtraUsageRow(source.ExtraUsage);

        if (_rows.Count == 0)
        {
            _statusText = "このプランでは使用率が返りませんでした。";
        }

        RelayoutAndRedraw();
    }

    private void AddWindowRow(string label, UsageWindow? window)
    {
        // プランによっては null で返る。その場合は行ごと出さない。
        if (window is null) return;

        double? percent = _config.ToPercent(window.Utilization);
        string sub = ThresholdNotifier.FormatReset(window.ResetsAt);

        _rows.Add(new Row(label, percent, sub));
    }

    private void AddExtraUsageRow(ExtraUsage? extra)
    {
        if (extra is null) return;

        if (!extra.IsEnabled)
        {
            string reason = string.IsNullOrWhiteSpace(extra.DisabledReason)
                ? "無効です。"
                : extra.DisabledReason;

            _rows.Add(new Row("追加課金枠", null, reason, Muted: true));
            return;
        }

        double? percent = _config.ToPercent(extra.Utilization);

        // used_credits / monthly_limit は最小通貨単位。
        // そのまま出すと 100 倍ずれるので decimal_places で割る。
        string sub;
        if (extra.UsedCredits is { } used && extra.MonthlyLimit is { } limit)
        {
            sub = $"{extra.FormatAmount(used)} / {extra.FormatAmount(limit)}";
            if (extra.SpendLimitReached) sub += "（上限到達）";
        }
        else if (extra.UsedCredits is { } usedOnly)
        {
            sub = $"{extra.FormatAmount(usedOnly)} 使用";
        }
        else
        {
            sub = "上限は未設定です。";
        }

        _rows.Add(new Row("追加課金枠", percent, sub));
    }

    private void RelayoutAndRedraw()
    {
        ApplyTheme();

        int contentHeight = _rows.Count > 0
            ? _rows.Count * RowHeight
            : 76;

        Height = HeaderHeight + contentHeight + FooterHeight;

        int buttonY = Height - 36;
        _exitButton.Location = new Point(PanelWidth - PaddingX - _exitButton.Width, buttonY);
        _settingsButton.Location =
            new Point(_exitButton.Left - _settingsButton.Width - 6, buttonY);
        _refreshButton.Location =
            new Point(_settingsButton.Left - _refreshButton.Width - 6, buttonY);

        UpdateRoundedRegions();
        Invalidate();
    }

    private void UpdateRoundedRegions()
    {
        ApplyRoundedRegion(this, PanelCornerRadius);
    }

    private static void ApplyRoundedRegion(Control control, float radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return;

        using GraphicsPath path = RoundedRect(
            new RectangleF(0, 0, control.Width, control.Height),
            radius);
        var next = new Region(path);
        Region? previous = control.Region;
        control.Region = next;
        previous?.Dispose();
    }

    // ---- テーマ ----

    private Color BackColorMain => _dark ? Color.FromArgb(0x20, 0x21, 0x24) : Color.White;
    private Color ForeColorMain => _dark ? Color.FromArgb(0xE8, 0xEA, 0xED) : Color.FromArgb(0x20, 0x21, 0x24);
    private Color SubColor => _dark ? Color.FromArgb(0x9A, 0xA0, 0xA6) : Color.FromArgb(0x5F, 0x63, 0x68);
    private Color TrackColor => _dark ? Color.FromArgb(0x3C, 0x40, 0x43) : Color.FromArgb(0xE8, 0xEA, 0xED);
    private Color BorderColor => _dark ? Color.FromArgb(0x5F, 0x63, 0x68) : Color.FromArgb(0xDA, 0xDC, 0xE0);

    private void ApplyTheme()
    {
        _dark = ThemeHelper.AreAppsDark();

        BackColor = BackColorMain;

        foreach (Button b in new[] { _refreshButton, _settingsButton, _exitButton })
        {
            b.BackColor = BackColorMain;
            b.ForeColor = ForeColorMain;
            b.FlatAppearance.BorderColor = BorderColor;
            b.FlatAppearance.MouseOverBackColor = TrackColor;
            b.FlatAppearance.MouseDownBackColor = _dark
                ? Color.FromArgb(0x4A, 0x4E, 0x52)
                : Color.FromArgb(0xD2, 0xD5, 0xD8);
        }

        _notificationsToggle.ApplyColors(
            BackColorMain,
            ForeColorMain,
            BorderColor,
            TrackColor,
            _dark
                ? Color.FromArgb(0x2C, 0x45, 0x38)
                : Color.FromArgb(0xE6, 0xF4, 0xEA));
    }

    private static Color BarColor(double percent) =>
        percent >= 100 ? Color.FromArgb(0xB3, 0x26, 0x1E)
        : percent > 80 ? Color.FromArgb(0xD9, 0x30, 0x25)
        : percent >= 50 ? Color.FromArgb(0xE5, 0xA5, 0x0A)
        : Color.FromArgb(0x22, 0xA4, 0x5D);

    // ---- 描画 ----

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var titleFont = new Font("Yu Gothic UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var labelFont = new Font("Yu Gothic UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        using var valueFont = new Font("Yu Gothic UI", 11f, FontStyle.Bold, GraphicsUnit.Point);
        using var subFont = new Font("Yu Gothic UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);

        using var fgBrush = new SolidBrush(ForeColorMain);
        using var subBrush = new SolidBrush(SubColor);
        using var trackBrush = new SolidBrush(TrackColor);

        // Windows 11 風の丸角の外枠
        using (var pen = new Pen(BorderColor))
        using (GraphicsPath outline = RoundedRect(
            new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
            PanelCornerRadius))
        {
            g.DrawPath(pen, outline);
        }

        // ヘッダー
        g.DrawString("Claude 使用状況", titleFont, fgBrush, PaddingX, 12);

        int y = HeaderHeight;

        if (_rows.Count == 0)
        {
            g.DrawString(_statusText, labelFont, subBrush,
                new RectangleF(PaddingX, y + 4, PanelWidth - PaddingX * 2, 40));

            if (_diagnostic.Length > 0)
            {
                g.DrawString(_diagnostic, subFont, subBrush,
                    new RectangleF(PaddingX, y + 44, PanelWidth - PaddingX * 2, 28));
            }

            return;
        }

        foreach (Row row in _rows)
        {
            DrawRow(g, row, y, labelFont, valueFont, subFont, fgBrush, subBrush, trackBrush);
            y += RowHeight;
        }

        // フッターの状態テキスト（必要なら診断行も）
        DrawFooter(g, subFont, subBrush);
    }

    private void DrawFooter(Graphics g, Font subFont, Brush subBrush)
    {
        int footerTop = Height - FooterHeight + 6;
        int available = PanelWidth - PaddingX * 2;

        // 古い値を表示中であることは色でも伝える
        Brush statusBrush = _isStale
            ? new SolidBrush(Color.FromArgb(0xE5, 0xA5, 0x0A))
            : subBrush;

        try
        {
            g.DrawString(
                _statusText, subFont, statusBrush,
                new RectangleF(PaddingX, footerTop, available, 26));
        }
        finally
        {
            if (_isStale) statusBrush.Dispose();
        }

        if (_diagnostic.Length > 0)
        {
            g.DrawString(
                _diagnostic, subFont, subBrush,
                new RectangleF(PaddingX, footerTop + 15, available, 14));
        }
    }

    private void DrawRow(
        Graphics g, Row row, int top,
        Font labelFont, Font valueFont, Font subFont,
        Brush fgBrush, Brush subBrush, Brush trackBrush)
    {
        int contentWidth = PanelWidth - PaddingX * 2;

        Brush labelBrush = row.Muted ? subBrush : fgBrush;
        g.DrawString(row.Label, labelFont, labelBrush, PaddingX, top + 4);

        // 右寄せの数値
        string valueText = row.Percent is null ? "—" : $"{row.Percent.Value:0}%";
        SizeF valueSize = g.MeasureString(valueText, valueFont);
        Brush valueBrush = row.Percent is null
            ? subBrush
            : new SolidBrush(BarColor(row.Percent.Value));

        try
        {
            g.DrawString(
                valueText, valueFont, valueBrush,
                PanelWidth - PaddingX - valueSize.Width, top + 1);
        }
        finally
        {
            if (row.Percent is not null) valueBrush.Dispose();
        }

        // バー
        int barY = top + 26;
        const int barHeight = 6;
        var track = new Rectangle(PaddingX, barY, contentWidth, barHeight);

        using (var path = RoundedRect(track, barHeight / 2f))
        {
            g.FillPath(trackBrush, path);
        }

        if (row.Percent is > 0)
        {
            int filled = (int)Math.Round(contentWidth * Math.Min(row.Percent.Value, 100) / 100.0);
            filled = Math.Max(filled, barHeight); // 極小値でも丸みが潰れないように

            var fill = new Rectangle(PaddingX, barY, filled, barHeight);
            using var path = RoundedRect(fill, barHeight / 2f);
            using var brush = new SolidBrush(BarColor(row.Percent.Value));
            g.FillPath(brush, path);
        }

        g.DrawString(row.Sub, subFont, subBrush, PaddingX - 2, barY + barHeight + 2);
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();

        if (d <= 0 || rect.Width < d)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    // ---- 表示位置 ----

    /// <summary>カーソル位置のモニタで、タスクバーの反対側に寄せて表示する。</summary>
    public void ShowNearTray()
    {
        UpdateRoundedRegions();

        Point cursor = Cursor.Position;
        Screen screen = Screen.FromPoint(cursor);
        Rectangle work = screen.WorkingArea;
        Rectangle full = screen.Bounds;

        const int Margin = 8;

        bool taskbarAtBottom = work.Bottom < full.Bottom;
        bool taskbarAtTop = work.Top > full.Top;

        int x = Math.Clamp(
            cursor.X - Width / 2,
            work.Left + Margin,
            Math.Max(work.Left + Margin, work.Right - Width - Margin));

        int y;
        if (taskbarAtTop)
        {
            y = work.Top + Margin;
        }
        else if (taskbarAtBottom)
        {
            y = work.Bottom - Height - Margin;
        }
        else
        {
            // タスクバーが左右にある場合はカーソル高さに合わせる
            y = Math.Clamp(
                cursor.Y - Height / 2,
                work.Top + Margin,
                Math.Max(work.Top + Margin, work.Bottom - Height - Margin));
        }

        Location = new Point(x, y);

        Show();
        Activate();
        BringToFront();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Hide();
    }

    /// <summary>ユーザーの「終了」以外ではフォームを破棄せず隠すだけにする。</summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}
