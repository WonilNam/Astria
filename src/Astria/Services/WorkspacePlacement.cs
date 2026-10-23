using Avalonia;

namespace Astria.Desktop.Services;

public readonly record struct WorkspaceBounds(double Width, double Height, PixelPoint Position);

public static class WorkspacePlacement
{
    /// <summary>Keep the entire window in the work area, including small high-DPI and negative-origin displays.</summary>
    public static WorkspaceBounds Fit(PixelRect workArea, double scaling, double width, double height, PixelPoint position)
    {
        if (!double.IsFinite(scaling) || scaling <= 0) throw new ArgumentOutOfRangeException(nameof(scaling));
        var availableWidth = Math.Max(1, workArea.Width - 24 * scaling);
        var availableHeight = Math.Max(1, workArea.Height - 24 * scaling);
        var fittedWidth = Math.Clamp(double.IsFinite(width) ? width : 1152, 1, availableWidth / scaling);
        var fittedHeight = Math.Clamp(double.IsFinite(height) ? height : 800, 1, availableHeight / scaling);
        var pixelWidth = (int)Math.Ceiling(fittedWidth * scaling);
        var pixelHeight = (int)Math.Ceiling(fittedHeight * scaling);
        var inset = Math.Min((int)(12 * scaling), Math.Min(workArea.Width, workArea.Height) / 4);
        var x = Math.Clamp(position.X, workArea.X + inset, Math.Max(workArea.X + inset, workArea.Right - pixelWidth - inset));
        var y = Math.Clamp(position.Y, workArea.Y + inset, Math.Max(workArea.Y + inset, workArea.Bottom - pixelHeight - inset));
        return new(fittedWidth, fittedHeight, new PixelPoint(x, y));
    }
}
