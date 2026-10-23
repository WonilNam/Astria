using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Astria.Desktop.Services;

namespace Astria.Desktop.Views;

/// <summary>Transparent cosmic field. Brushes are reused across animation frames.</summary>
public sealed class StarRingScene : Control
{
    private static RadialGradientBrush Radial(string center, string middle) => new()
    {
        GradientStops = { new GradientStop(Color.Parse(center), 0), new GradientStop(Color.Parse(middle), .55), new GradientStop(Colors.Transparent, 1) }
    };
    private static readonly IBrush Veil = new RadialGradientBrush
    {
        GradientStops = { new GradientStop(Color.Parse("#20080D1A"), 0), new GradientStop(Color.Parse("#40050814"), .55), new GradientStop(Color.Parse("#6803050B"), 1) }
    };
    private IBrush _lavender = Radial("#728E55FF", "#405C32B8");
    private IBrush _ocean = Radial("#653BA8FF", "#351C68C8");
    private IBrush _core = Radial("#50B58AFF", "#10B58AFF");
    public void SetTheme(string theme)
    {
        var palette = DesktopPalette.For(theme);
        _lavender = Radial("#72" + palette.Glow[1..], "#30" + palette.Glow[1..]);
        _ocean = Radial("#65" + palette.Secondary[1..], "#28" + palette.Secondary[1..]);
        _core = Radial("#45" + palette.Accent[1..], "#08" + palette.Accent[1..]);
        InvalidateVisual();
    }
    private static readonly IBrush Star = Brush.Parse("#FFFFFFFF");
    private static readonly IBrush Halo = Brush.Parse("#809EC5FF");
    private static readonly IBrush Particle = Brush.Parse("#D0D8FFFF");
    private static readonly Pen Track = new(Brush.Parse("#4578B8FF"), 1.4);
    public StarRingScene(OrbitMotion orbit) { IsHitTestVisible = false; }
    public bool Immersive { get; set; }
    public double SceneScale { get; set; } = 1;
    public double Phase { get; set; }
    public string Formation { get; set; } = "";
    public double FormationRadius { get; set; }
    public int FormationCount { get; set; }
    public double FormationRotation { get; set; }
    public double FormationOffsetX { get; set; }
    public Point? SidebarAnchor { get; set; }
    public double SidebarSpanY { get; set; }
    public double SidebarArcDepth { get; set; }
    private static readonly Pen FormationTrack = new(Brush.Parse("#287E9BCE"), 1);
    private static readonly Pen WingTrack = new(Brush.Parse("#3690B8F8"), 1.2);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        var center = new Point(Bounds.Width / 2 + FormationOffsetX, Bounds.Height / 2);
        var scale = SceneScale;
        context.DrawRectangle(Veil, null, new Rect(Bounds.Size));
        context.DrawEllipse(_lavender, null, center, 390 * scale, 390 * scale);
        context.DrawEllipse(_ocean, null, center, 280 * scale, 280 * scale);
        context.DrawEllipse(_core, null, center, 160 * scale, 160 * scale);
        if (SidebarAnchor is { } wingCenter && SidebarSpanY > 10)
        {
            context.DrawEllipse(_ocean, null, wingCenter, 110 * scale, Math.Max(110 * scale, SidebarSpanY * .6));
        }
        if (Formation == "orbits")
        {
            context.DrawEllipse(null, FormationTrack, center, FormationRadius, FormationRadius);
            if (FormationCount > 8) context.DrawEllipse(null, FormationTrack, center, FormationRadius * .42, FormationRadius * .42);
        }
        else if (Formation == "galaxy")
        {
            for (var arm = 0; arm < 2; arm++)
            {
                var path = new StreamGeometry();
                using (var geometry = path.Open())
                {
                    for (var step = 0; step <= 40; step++)
                    {
                        var point = ConstellationLayout.GalaxyPoint(step / 40d, arm, FormationRadius, FormationRotation) + center;
                        if (step == 0) geometry.BeginFigure(point, false); else geometry.LineTo(point);
                    }
                    geometry.EndFigure(false);
                }
                context.DrawGeometry(null, FormationTrack, path);
            }
        }
        for (var i = 0; i < 64; i++)
        {
            var sx = Math.Sin(i * 12.9898 + 78.233) * 43758.5453 % 1;
            var sy = Math.Cos(i * 93.9898 + 67.345) * 24634.6345 % 1;
            var point = new Point((sx - Math.Floor(sx)) * Bounds.Width, (sy - Math.Floor(sy)) * Bounds.Height);
            var radius = (i % 8 == 0 ? 2.6 : i % 3 == 0 ? 1.7 : 1) * Math.Clamp(scale, .7, 1.2);
            using (context.PushOpacity(.42 + .52 * (1 + Math.Sin(Phase * 1.8 + i * 1.35)) / 2))
            {
                if (i % 8 == 0) context.DrawEllipse(Halo, null, point, radius * 2.2, radius * 2.2);
                context.DrawEllipse(Star, null, point, radius, radius);
            }
        }
        for (var i = 0; i < 24; i++)
        {
            var angle = i * 2.399963 + Phase * .04;
            var radius = 90 + i * 59 % 420;
            var point = new Point(center.X + Math.Cos(angle) * radius * scale + Math.Sin(Phase * .35 + i) * 16 * scale,
                center.Y + Math.Sin(angle) * radius * scale + Math.Cos(Phase * .28 + i * 1.3) * 16 * scale);
            using (context.PushOpacity(.35 + .50 * (1 + Math.Sin(Phase * 1.3 + i * .85)) / 2))
            {
                var size = (i % 6 == 0 ? 2.4 : 1.3) * scale;
                context.DrawEllipse(Particle, null, point, size, size);
            }
        }
        var wave = (140 + Phase * 24 % 260) * scale;
        using (context.PushOpacity(Math.Clamp(.4 * (1 - wave / (420 * scale)), 0, .4)))
            context.DrawEllipse(null, Track, center, wave, wave);
    }
}
