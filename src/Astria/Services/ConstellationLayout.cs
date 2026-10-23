using Avalonia;

namespace Astria.Desktop.Services;

public static class ConstellationLayout
{
    public static double MinimumRadius(int count, double spacing, string shape)
    {
        if (count <= 1) return 0;
        var distance = double.MaxValue;
        var phases = shape is "orbits" or "galaxy" ? 24 : 1;
        for (var phase = 0; phase < phases; phase++)
        {
            var points = Enumerable.Range(0, count).Select(index => Position(index, count, 1, phase * Math.Tau / phases, shape)).ToArray();
            for (var i = 0; i < points.Length; i++)
                for (var j = i + 1; j < points.Length; j++) distance = Math.Min(distance, Point.Distance(points[i], points[j]));
        }
        return spacing / Math.Max(.001, distance);
    }
    public static string Name(int count) => count switch { <= 1 => "独星", 2 => "双星", 3 => "三角", 4 => "菱形", <= 8 => "星环", _ => "星阵" };

    public static Point GalaxyPoint(double progress, int arm, double radius, double rotation)
    {
        var angle = -1.4 + progress * 4.2 + arm * Math.PI;
        var distance = radius * (.28 + .72 * progress);
        var x = Math.Cos(angle) * distance;
        var y = Math.Sin(angle) * distance * .68;
        var turn = Math.Sin(rotation * .45) * .12;
        return new Point(x * Math.Cos(turn) - y * Math.Sin(turn), x * Math.Sin(turn) + y * Math.Cos(turn));
    }

    public static Point Position(int index, int count, double radius, double rotation, string shape = "auto")
    {
        if (count <= 1) return default;
        double x, y;
        if (shape == "matrix")
        {
            var columns = count <= 3 ? count : count <= 9 ? 3 : count <= 12 ? 4 : count <= 20 ? 5 : 6;
            var rows = (count + columns - 1) / columns;
            var row = 0;
            var local = index;
            var inRow = count / rows + (row < count % rows ? 1 : 0);
            while (local >= inRow)
            {
                local -= inRow; row++;
                inRow = count / rows + (row < count % rows ? 1 : 0);
            }
            x = (local - (inRow - 1) / 2d) * radius * .58;
            y = (row - (rows - 1) / 2d) * radius * .68;
            return new Point(x, y);
        }
        else if (shape == "galaxy")
        {
            var arm = index % 2;
            var length = (count + 1 - arm) / 2;
            var progress = length <= 1 ? .5 : (index / 2) / (double)(length - 1);
            return GalaxyPoint(progress, arm, radius, rotation);
        }
        else if (shape == "orbits")
        {
            var innerCount = count <= 5 ? 0 : count <= 8 ? 1 : Math.Max(2, count / 4);
            var inner = index < innerCount;
            var length = inner ? innerCount : count - innerCount;
            var slot = inner ? index : index - innerCount;
            var angle = slot * Math.Tau / length - Math.PI / 2 + (inner ? Math.PI / length - rotation * .7 : rotation);
            var distance = inner && innerCount == 1 ? 0 : radius * (inner ? .42 : 1);
            x = Math.Cos(angle) * distance; y = Math.Sin(angle) * distance;
            return new Point(x, y);
        }
        else if (shape == "honeycomb" || shape == "auto" && count > 8)
        {
            var columns = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(count * 1.5)));
            var rows = (count + columns - 1) / columns;
            var row = index / columns;
            var inRow = Math.Min(columns, count - row * columns);
            x = (index % columns - (inRow - 1) / 2d + (row % 2 == 0 ? -.2 : .2)) * radius * .6;
            y = (row - (rows - 1) / 2d) * radius * .6;
        }
        else if (shape == "wave")
        {
            var columns = Math.Min(count, 4);
            var row = index / columns;
            var rows = (count + columns - 1) / columns;
            var inRow = Math.Min(columns, count - row * columns);
            x = (index % columns - (inRow - 1) / 2d) * radius * .58;
            y = (row - (rows - 1) / 2d) * radius * .65 + Math.Sin(index % columns * 1.4) * radius * .08;
        }
        else if (shape == "arcs")
        {
            var half = (count + 1) / 2; var side = index < half ? -1 : 1;
            var local = index < half ? index : index - half;
            var length = index < half ? half : count - half;
            var angle = length <= 1 ? 0 : (local / (double)(length - 1) - .5) * Math.PI * .8;
            x = side * (radius * .5 + Math.Cos(angle) * radius * .4);
            y = Math.Sin(angle) * radius;
        }
        else if (shape == "spiral")
        {
            var angle = index * 2.399963;
            var distance = Math.Sqrt((index + .6) / count) * radius;
            x = Math.Cos(angle) * distance; y = Math.Sin(angle) * distance;
        }
        else if (shape == "circle" || shape == "auto" && count <= 8)
        {
            var angle = index * Math.Tau / count - (count == 2 ? 0 : Math.PI / 2);
            var size = radius;
            x = Math.Cos(angle) * size; y = Math.Sin(angle) * size;
        }
        else
        {
            var columns = count > 12 ? Math.Min(6, (int)Math.Ceiling(Math.Sqrt(count * 1.5))) : 4;
            var rows = (count + columns - 1) / columns;
            var row = index / columns;
            var inRow = Math.Min(columns, count - row * columns);
            x = (index % columns - (inRow - 1) / 2d) * radius * .56;
            y = (row - (rows - 1) / 2d) * radius * .59;
        }
        return new Point(x * Math.Cos(rotation) - y * Math.Sin(rotation), x * Math.Sin(rotation) + y * Math.Cos(rotation));
    }
}
