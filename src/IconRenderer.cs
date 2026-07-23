using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

public enum IconState
{
    Normal,
    Full,

    /// <summary>直近の成功値を表示中。取得は失敗しているが行動は不要。</summary>
    Stale,

    /// <summary>再ログインが必要など、ユーザーの操作を要する状態。</summary>
    Error,

    Unknown,
}

/// <summary>
/// 通知領域アイコンを毎回 GDI+ で描き直す。
///
/// 重要: Bitmap.GetHicon() が返す HICON は GDI オブジェクトで、GC では解放されない。
/// 3 分おきに生成するため、DestroyIcon を確実に呼ばないと数日でハンドルが枯渇する。
/// </summary>
public static class IconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    // 色分け: 緑 <50 / 黄 50-80 / 赤 >80
    private static readonly Color ColorOk = Color.FromArgb(0x22, 0xA4, 0x5D);
    private static readonly Color ColorWarn = Color.FromArgb(0xE5, 0xA5, 0x0A);
    private static readonly Color ColorDanger = Color.FromArgb(0xD9, 0x30, 0x25);
    private static readonly Color ColorFull = Color.FromArgb(0xB3, 0x26, 0x1E);
    private static readonly Color ColorInactive = Color.FromArgb(0x5F, 0x63, 0x68);

    /// <summary>古い値を表示中であることを示すグレー。Inactive よりわずかに明るくする。</summary>
    private static readonly Color ColorStale = Color.FromArgb(0x7A, 0x7F, 0x85);

    private static readonly Color TextLight = Color.White;
    private static readonly Color TextDark = Color.FromArgb(0x1A, 0x1A, 0x1A);

    /// <summary>現在の通知領域アイコンの一辺（px）。DPI に応じて 16/20/24/32 相当になる。</summary>
    public static int CurrentIconSize
    {
        get
        {
            int size = SystemInformation.SmallIconSize.Width;
            return Math.Clamp(size, 16, 64);
        }
    }

    /// <summary>
    /// アイコンを生成する。呼び出し側は差し替え時に古い Icon を Dispose すること。
    /// </summary>
    /// <param name="percent">5 時間枠の使用率（0〜100）。不明なら null。</param>
    /// <param name="state">表示状態。</param>
    /// <param name="darkTaskbar">タスクバーがダークテーマかどうか（縁取りの調整に使う）。</param>
    public static Icon Render(double? percent, IconState state, bool darkTaskbar)
    {
        int size = CurrentIconSize;

        // 小さいサイズで文字が潰れないよう 4 倍で描いて縮小する
        const int Supersample = 4;
        int big = size * Supersample;

        using var bmp = new Bitmap(big, big, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            (Color fill, string text) = Describe(percent, state);
            Color textColor = NeedsDarkText(fill) ? TextDark : TextLight;

            float inset = big * 0.02f;
            var rect = new RectangleF(inset, inset, big - inset * 2, big - inset * 2);
            float radius = big * 0.24f;

            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);

                // ライトテーマのタスクバーでは輪郭が背景に溶けやすいので薄く縁取る
                if (!darkTaskbar)
                {
                    using var pen = new Pen(Color.FromArgb(70, 0, 0, 0), big * 0.03f);
                    g.DrawPath(pen, path);
                }
            }

            if (text.Length > 0)
            {
                DrawFittedText(g, text, rect, textColor, big);
            }
        }

        // 4 倍のビットマップをアイコンサイズへ縮小
        using var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(bmp, new Rectangle(0, 0, size, size));
        }

        IntPtr hIcon = scaled.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            // Icon.FromHandle はハンドルを所有しないので必ず自分で破棄する
            DestroyIcon(hIcon);
        }
    }

    private static (Color Fill, string Text) Describe(double? percent, IconState state)
    {
        switch (state)
        {
            case IconState.Error:
                return (ColorInactive, "×");

            case IconState.Unknown:
                return (ColorInactive, "?");

            case IconState.Stale:
                // 数値は残しつつグレーで「古い値」であることを伝える
                if (percent is null) return (ColorInactive, "?");
                int stale = (int)Math.Round(percent.Value, MidpointRounding.AwayFromZero);
                return (ColorStale, stale >= 100 ? "!!" : stale.ToString());

            case IconState.Full:
                return (ColorFull, "!!");

            default:
                if (percent is null) return (ColorInactive, "?");

                int rounded = (int)Math.Round(percent.Value, MidpointRounding.AwayFromZero);

                if (rounded >= 100) return (ColorFull, "!!");

                Color fill = rounded > 80 ? ColorDanger
                    : rounded >= 50 ? ColorWarn
                    : ColorOk;

                return (fill, rounded.ToString());
        }
    }

    private static bool NeedsDarkText(Color background)
    {
        // 相対輝度が高い（＝黄色系）なら黒文字にする
        double luminance =
            (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        return luminance > 0.55;
    }

    private static void DrawFittedText(
        Graphics g, string text, RectangleF bounds, Color color, int canvasSize)
    {
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None,
        };

        // 収まる最大のフォントサイズを探す
        float maxWidth = bounds.Width * 0.84f;
        float maxHeight = bounds.Height * 0.84f;
        float fontSize = canvasSize * 0.72f;

        Font? chosen = null;
        try
        {
            while (fontSize > 4f)
            {
                var candidate = new Font(
                    "Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                SizeF measured = g.MeasureString(text, candidate, PointF.Empty, format);

                if (measured.Width <= maxWidth && measured.Height <= maxHeight)
                {
                    chosen = candidate;
                    break;
                }

                candidate.Dispose();
                fontSize *= 0.92f;
            }

            chosen ??= new Font("Segoe UI", 5f, FontStyle.Bold, GraphicsUnit.Pixel);

            using var brush = new SolidBrush(color);
            g.DrawString(text, chosen, brush, bounds, format);
        }
        finally
        {
            chosen?.Dispose();
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();

        if (d <= 0)
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
}
