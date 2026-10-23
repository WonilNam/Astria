namespace Astria.Domain;

public readonly record struct CardPose(double X, double Y, double Scale, double Tilt);
public static class Formations
{
    // Produce a normalized composition first; fit the complete cards, not only their centers.
    public static IReadOnlyList<CardPose> Arrange(string kind, int count, double width, double height, double phase, double size = 1)
    {
        if (count <= 0 || width <= 0 || height <= 0) return [];
        var points = new List<(double X, double Y, double Tilt)>();
        if (kind == "matrix" || count <= 3)
        {
            var columns = Math.Min(count, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count * 1.65))));
            var rows = (int)Math.Ceiling((double)count / columns);
            for (int row = 0, index = 0; row < rows; row++)
            {
                var remaining = count - index; var cells = (int)Math.Ceiling((double)remaining / (rows - row));
                for (var col = 0; col < cells; col++, index++) points.Add(((col - (cells - 1) / 2d) * 194, (row - (rows - 1) / 2d) * 216, Math.Sin(phase + index) * 1.8));
            }
        }
        else if (kind == "galaxy")
        {
            // Two independently readable arcs, separated through the middle.
            for (var i = 0; i < count; i++)
            {
                var lane = i % 2; var slot = i / 2; var length = (count + 1 - lane) / 2;
                var t = length == 1 ? .5 : (double)slot / (length - 1);
                points.Add(((t - .5) * Math.Max(480, (length - 1) * 180), (lane == 0 ? -1 : 1) * (130 + 110 * Math.Sin(t * Math.PI)), (lane == 0 ? -1 : 1) * Math.Cos(t * Math.PI) * 4));
            }
        }
        else
        {
            var inner = count > 10 ? count / 3 : 0; var outer = count - inner;
            for (var i = 0; i < count; i++)
            {
                var inside = i < inner; var ring = inside ? inner : outer; var n = inside ? i : i - inner;
                var radius = inside ? Math.Max(155, inner * 184 / (2 * Math.PI)) : Math.Max(340, outer * 194 / (2 * Math.PI));
                var angle = 2 * Math.PI * n / ring - Math.PI / 2 + (inside ? -phase * .12 : phase * .08);
                points.Add((Math.Cos(angle) * radius, Math.Sin(angle) * radius, Math.Sin(angle + phase) * 3));
            }
        }
        var localScale = size;
        for (var i = 0; i < points.Count; i++) for (var j = i + 1; j < points.Count; j++)
            localScale = Math.Min(localScale, Math.Max(Math.Abs(points[i].X - points[j].X) / 166, Math.Abs(points[i].Y - points[j].Y) / 192));
        var left = points.Min(p => p.X) - 86 * localScale; var right = points.Max(p => p.X) + 86 * localScale;
        var top = points.Min(p => p.Y) - 100 * localScale; var bottom = points.Max(p => p.Y) + 100 * localScale;
        var fit = Math.Min(1, Math.Min(width / (right - left), height / (bottom - top)));
        return points.Select((p, i) => new CardPose((p.X - (left + right) / 2) * fit + width / 2,
            (p.Y - (top + bottom) / 2) * fit + height / 2 + Math.Sin(phase + i * 1.7) * 3 * fit, localScale * fit, p.Tilt)).ToArray();
    }
}

