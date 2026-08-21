using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ClaudeUsageTray;

/// <summary>
/// 埋め込み SVG のパスを GraphicsPath に変換する軽量アイコンローダー。
/// Lucide アイコンで使う M / L / H / C / A / Z（絶対・相対座標）に対応し、
/// 汎用 SVG ライブラリの依存を避ける。
/// </summary>
internal sealed class SvgPathIcon : IDisposable
{
    private static readonly Regex NumberRegex = new(
        @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TokenRegex = new(
        @"[A-Za-z]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?",
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
            }

            bool relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                {
                    PointF point = ReadPoint(tokens, ref index);
                    current = relative ? Offset(current, point) : point;
                    figureStart = current;
                    path.StartFigure();
                    command = relative ? 'l' : 'L';
                    break;
                }

                case 'L':
                {
                    PointF end = ReadPoint(tokens, ref index);
                    if (relative) end = Offset(current, end);
                    path.AddLine(current, end);
                    current = end;
                    break;
                }

                case 'H':
                {
                    float x = ReadNumber(tokens, ref index);
                    PointF end = new(relative ? current.X + x : x, current.Y);
                    path.AddLine(current, end);
                    current = end;
                    break;
                }

                case 'C':
                {
                    PointF control1 = ReadPoint(tokens, ref index);
                    PointF control2 = ReadPoint(tokens, ref index);
                    PointF end = ReadPoint(tokens, ref index);
                    if (relative)
                    {
                        control1 = Offset(current, control1);
                        control2 = Offset(current, control2);
                        end = Offset(current, end);
                    }
                    path.AddBezier(current, control1, control2, end);
                    current = end;
                    break;
                }

                case 'A':
                {
                    float radiusX = ReadNumber(tokens, ref index);
                    float radiusY = ReadNumber(tokens, ref index);
                    float rotation = ReadNumber(tokens, ref index);
                    bool largeArc = ReadFlag(tokens, ref index);
                    bool sweep = ReadFlag(tokens, ref index);
                    PointF end = ReadPoint(tokens, ref index);
                    if (relative) end = Offset(current, end);

                    AppendArc(path, current, end, radiusX, radiusY, rotation, largeArc, sweep);
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
        float x = ReadNumber(tokens, ref index);
        float y = ReadNumber(tokens, ref index);
        return new PointF(x, y);
    }

    private static float ReadNumber(IReadOnlyList<string> tokens, ref int index)
    {
        if (index >= tokens.Count || IsCommand(tokens[index]))
        {
            throw new FormatException("SVG path の座標が不足しています。");
        }

        return float.Parse(tokens[index++], CultureInfo.InvariantCulture);
    }

    private static bool ReadFlag(IReadOnlyList<string> tokens, ref int index)
    {
        float value = ReadNumber(tokens, ref index);
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException($"SVG arc のフラグが不正です: {value}"),
        };
    }

    private static PointF Offset(PointF origin, PointF delta) =>
        new(origin.X + delta.X, origin.Y + delta.Y);

    /// <summary>SVG の楕円弧を 90 度以下の 3 次ベジェ曲線に分割する。</summary>
    private static void AppendArc(
        GraphicsPath path,
        PointF start,
        PointF end,
        float radiusX,
        float radiusY,
        float rotationDegrees,
        bool largeArc,
        bool sweep)
    {
        double rx = Math.Abs(radiusX);
        double ry = Math.Abs(radiusY);

        if (rx == 0 || ry == 0)
        {
            path.AddLine(start, end);
            return;
        }

        if (Math.Abs(start.X - end.X) < float.Epsilon
            && Math.Abs(start.Y - end.Y) < float.Epsilon)
        {
            return;
        }

        double phi = rotationDegrees * Math.PI / 180d;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);
        double halfDx = (start.X - end.X) / 2d;
        double halfDy = (start.Y - end.Y) / 2d;
        double x1Prime = cosPhi * halfDx + sinPhi * halfDy;
        double y1Prime = -sinPhi * halfDx + cosPhi * halfDy;

        double radiiScale = x1Prime * x1Prime / (rx * rx)
            + y1Prime * y1Prime / (ry * ry);
        if (radiiScale > 1)
        {
            double scale = Math.Sqrt(radiiScale);
            rx *= scale;
            ry *= scale;
        }

        double rxSquared = rx * rx;
        double rySquared = ry * ry;
        double x1Squared = x1Prime * x1Prime;
        double y1Squared = y1Prime * y1Prime;
        double denominator = rxSquared * y1Squared + rySquared * x1Squared;
        double numerator = Math.Max(
            0,
            rxSquared * rySquared - rxSquared * y1Squared - rySquared * x1Squared);
        double coefficient = denominator == 0
            ? 0
            : (largeArc == sweep ? -1d : 1d) * Math.Sqrt(numerator / denominator);

        double centerXPrime = coefficient * rx * y1Prime / ry;
        double centerYPrime = coefficient * -ry * x1Prime / rx;
        double centerX = cosPhi * centerXPrime - sinPhi * centerYPrime
            + (start.X + end.X) / 2d;
        double centerY = sinPhi * centerXPrime + cosPhi * centerYPrime
            + (start.Y + end.Y) / 2d;

        double ux = (x1Prime - centerXPrime) / rx;
        double uy = (y1Prime - centerYPrime) / ry;
        double vx = (-x1Prime - centerXPrime) / rx;
        double vy = (-y1Prime - centerYPrime) / ry;
        double startAngle = Math.Atan2(uy, ux);
        double sweepAngle = VectorAngle(ux, uy, vx, vy);

        if (!sweep && sweepAngle > 0) sweepAngle -= Math.PI * 2d;
        if (sweep && sweepAngle < 0) sweepAngle += Math.PI * 2d;

        int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2d)));
        double segmentAngle = sweepAngle / segments;
        PointF segmentStart = start;

        for (int segment = 0; segment < segments; segment++)
        {
            double angle1 = startAngle + segment * segmentAngle;
            double angle2 = angle1 + segmentAngle;
            double alpha = 4d / 3d * Math.Tan((angle2 - angle1) / 4d);

            PointF control1 = MapEllipsePoint(
                Math.Cos(angle1) - alpha * Math.Sin(angle1),
                Math.Sin(angle1) + alpha * Math.Cos(angle1),
                rx, ry, cosPhi, sinPhi, centerX, centerY);
            PointF control2 = MapEllipsePoint(
                Math.Cos(angle2) + alpha * Math.Sin(angle2),
                Math.Sin(angle2) - alpha * Math.Cos(angle2),
                rx, ry, cosPhi, sinPhi, centerX, centerY);
            PointF segmentEnd = segment == segments - 1
                ? end
                : MapEllipsePoint(
                    Math.Cos(angle2),
                    Math.Sin(angle2),
                    rx, ry, cosPhi, sinPhi, centerX, centerY);

            path.AddBezier(segmentStart, control1, control2, segmentEnd);
            segmentStart = segmentEnd;
        }
    }

    private static double VectorAngle(double ux, double uy, double vx, double vy) =>
        Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);

    private static PointF MapEllipsePoint(
        double unitX,
        double unitY,
        double radiusX,
        double radiusY,
        double cosRotation,
        double sinRotation,
        double centerX,
        double centerY) =>
        new(
            (float)(centerX + cosRotation * radiusX * unitX - sinRotation * radiusY * unitY),
            (float)(centerY + sinRotation * radiusX * unitX + cosRotation * radiusY * unitY));

    private static bool IsCommand(string token) =>
        token.Length == 1 && char.IsLetter(token[0]);
}
