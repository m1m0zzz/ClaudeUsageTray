using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClaudeUsageTray;

/// <summary>
/// 埋め込み SVG のパスを GraphicsPath に変換する軽量アイコンローダー。
/// このアプリのアイコンで使う絶対座標の M / L / C / Z に限定し、
/// 汎用 SVG ライブラリの依存を避ける。
/// </summary>
internal sealed class SvgPathIcon : IDisposable
{
    private static readonly Regex NumberRegex = new(
        @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TokenRegex = new(
        @"[MLCZmlcz]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly GraphicsPath _path;
    private readonly RectangleF _viewBox;

    private SvgPathIcon(GraphicsPath path, RectangleF viewBox)
    {
        _path = path;
        _viewBox = viewBox;
    }

    public static SvgPathIcon Load(string resourceName)
    {
        Assembly assembly = typeof(SvgPathIcon).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"SVG リソースが見つかりません: {resourceName}");

        XDocument document = XDocument.Load(stream);
        XElement root = document.Root
            ?? throw new FormatException("SVG にルート要素がありません。");

        RectangleF viewBox = ParseViewBox((string?)root.Attribute("viewBox"));
        var combined = new GraphicsPath();

        try
        {
            foreach (XElement element in root.Descendants()
                         .Where(element => element.Name.LocalName == "path"))
            {
                string? data = (string?)element.Attribute("d");
                if (!string.IsNullOrWhiteSpace(data))
                {
                    AppendPath(combined, data);
                }
            }

            if (combined.PointCount == 0)
            {
                throw new FormatException("SVG に描画可能な path がありません。");
            }

            return new SvgPathIcon(combined, viewBox);
        }
        catch
        {
            combined.Dispose();
            throw;
        }
    }

    public void Draw(Graphics graphics, Color color, RectangleF bounds, float strokeWidth)
    {
        float scale = Math.Min(bounds.Width / _viewBox.Width, bounds.Height / _viewBox.Height);
        float offsetX = bounds.X + (bounds.Width - _viewBox.Width * scale) / 2f;
        float offsetY = bounds.Y + (bounds.Height - _viewBox.Height * scale) / 2f;

        using var transform = new Matrix(
            scale,
            0,
            0,
            scale,
            offsetX - _viewBox.X * scale,
            offsetY - _viewBox.Y * scale);
        using var transformed = (GraphicsPath)_path.Clone();
        transformed.Transform(transform);

        using var pen = new Pen(color, strokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        graphics.DrawPath(pen, transformed);
    }

    public void Dispose() => _path.Dispose();

    private static RectangleF ParseViewBox(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("SVG に viewBox がありません。");
        }

        float[] values = NumberRegex.Matches(text)
            .Select(match => float.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();

        if (values.Length != 4 || values[2] <= 0 || values[3] <= 0)
        {
            throw new FormatException($"SVG の viewBox が不正です: {text}");
        }

        return new RectangleF(values[0], values[1], values[2], values[3]);
    }

    private static void AppendPath(GraphicsPath path, string data)
    {
        string[] tokens = TokenRegex.Matches(data)
            .Select(match => match.Value)
            .ToArray();

        int index = 0;
        char command = '\0';
        PointF current = default;
        PointF figureStart = default;

        while (index < tokens.Length)
        {
            if (IsCommand(tokens[index]))
            {
                command = tokens[index++][0];
                if (char.IsLower(command))
                {
                    throw new FormatException($"相対座標の SVG コマンドは未対応です: {command}");
                }
            }

            switch (command)
            {
                case 'M':
                    current = ReadPoint(tokens, ref index);
                    figureStart = current;
                    path.StartFigure();
                    command = 'L';
                    break;

                case 'L':
                {
                    PointF end = ReadPoint(tokens, ref index);
                    path.AddLine(current, end);
                    current = end;
                    break;
                }

                case 'C':
                {
                    PointF control1 = ReadPoint(tokens, ref index);
                    PointF control2 = ReadPoint(tokens, ref index);
                    PointF end = ReadPoint(tokens, ref index);
                    path.AddBezier(current, control1, control2, end);
                    current = end;
                    break;
                }

                case 'Z':
                    path.CloseFigure();
                    current = figureStart;
                    command = '\0';
                    break;

                default:
                    throw new FormatException("SVG path のコマンドが不正です。");
            }
        }
    }

    private static PointF ReadPoint(IReadOnlyList<string> tokens, ref int index)
    {
        if (index + 1 >= tokens.Count
            || IsCommand(tokens[index])
            || IsCommand(tokens[index + 1]))
        {
            throw new FormatException("SVG path の座標が不足しています。");
        }

        float x = float.Parse(tokens[index++], CultureInfo.InvariantCulture);
        float y = float.Parse(tokens[index++], CultureInfo.InvariantCulture);
        return new PointF(x, y);
    }

    private static bool IsCommand(string token) =>
        token.Length == 1 && char.IsLetter(token[0]);
}
