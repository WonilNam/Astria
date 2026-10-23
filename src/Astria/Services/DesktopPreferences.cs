using System.Collections.Immutable;
using System.Text.Json;
using Astria.Core.Storage;

namespace Astria.Desktop.Services;

public sealed record CardLocation(double X, double Y);

public sealed record DesktopPreferences
{
    public bool UseAcrylic { get; init; } = true;
    public bool AutoHideDock { get; init; }
    public bool HideDesktopIconsOnStartup { get; init; }
    public bool ImmersiveLaunchpad { get; init; } = true;
    public bool ReduceMotion { get; init; }
    public double DockScale { get; init; } = 1;
    public int FavoriteVisibleCount { get; init; } = 8;
    public string FavoriteShape { get; init; } = "auto";
    public string Accent { get; init; } = "lavender";
    public LaunchHotkey Hotkey { get; init; } = new();
    public LaunchHotkey SettingsHotkey { get; init; } = new(3, 83);
    public List<string> BackgroundRunningIds { get; init; } = [];
    public Dictionary<string, CardLocation> CardLocations { get; init; } = new(StringComparer.Ordinal);
    public int CardLayoutVersion { get; init; }
    public bool DefaultShellLocationsSeeded { get; init; }
    public int? GalleryLeft { get; init; }
    public int? GalleryTop { get; init; }

    public DesktopPreferences Normalize() => this with
    {
        DockScale = double.IsFinite(DockScale) ? Math.Clamp(DockScale, .75, 1.3) : 1,
        BackgroundRunningIds = (BackgroundRunningIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList(),
        FavoriteVisibleCount = Math.Clamp(FavoriteVisibleCount, 1, 24),
        FavoriteShape = FavoriteShape switch
        {
            "orbits" or "circle" => "orbits",
            "galaxy" or "spiral" or "arcs" => "galaxy",
            "matrix" or "grid" or "honeycomb" or "wave" => "matrix",
            _ => FavoriteVisibleCount > 8 ? "matrix" : "orbits"
        },
        Accent = Accent is "lavender" or "ocean" or "mint" or "aurora" or "neon" or "abyss" or "gold" ? Accent : "lavender",
        Hotkey = Hotkey is { IsValid: true } ? Hotkey : new(),
        SettingsHotkey = SettingsHotkey is { IsValid: true } && SettingsHotkey != Hotkey ? SettingsHotkey : Hotkey == new LaunchHotkey(3, 83) ? new(7, 83) : new(3, 83),
        CardLocations = (CardLocations ?? []).Where(pair => pair.Value is not null && double.IsFinite(pair.Value.X) && double.IsFinite(pair.Value.Y))
            .ToDictionary(pair => pair.Key, pair => new CardLocation(Math.Clamp(pair.Value.X, 0, 1), Math.Clamp(pair.Value.Y, 0, 1)), StringComparer.Ordinal),
    };
}

public sealed class DesktopPreferencesStore
{
    private readonly DesktopSession _session;
    public DesktopPreferencesStore(DesktopSession session)
    {
        _session = session;
        Current = Read();
    }
    public DesktopPreferences Current { get; private set; }
    public string? Notice => null;
    public void Save(DesktopPreferences value)
    {
        var next = value.Normalize();
        _session.Engine.SetSettings(_session.Engine.Current.Settings with {
            PresentationJson = System.Text.Json.JsonSerializer.Serialize(next, DesktopJsonContext.Default.DesktopPreferences),
            PageSize = next.FavoriteVisibleCount, Formation = next.FavoriteShape, CardScale = next.DockScale,
            Positions = next.CardLocations.Where(p=>Guid.TryParse(p.Key,out _)).ToImmutableDictionary(p=>Guid.Parse(p.Key),p=>new Astria.Domain.Position(p.Value.X,p.Value.Y)),
            HideDesktopIconsOnStartup = next.HideDesktopIconsOnStartup,
            DockKey = new(next.Hotkey.Modifiers,next.Hotkey.VirtualKey), SettingsKey = new(next.SettingsHotkey.Modifiers,next.SettingsHotkey.VirtualKey)
        }, next.BackgroundRunningIds.Where(id=>Guid.TryParse(id,out _)).Select(Guid.Parse).ToHashSet());
        Current = next;
    }
    internal void Reload() => Current = Read();
    private DesktopPreferences Read()
    {
        var settings=_session.Engine.Current.Settings;
        if(settings.PresentationJson!="{}")return System.Text.Json.JsonSerializer.Deserialize<DesktopPreferences>(settings.PresentationJson,DesktopJsonContext.Default.DesktopPreferences)?.Normalize()??new DesktopPreferences().Normalize();
        return new DesktopPreferences {FavoriteVisibleCount=settings.PageSize,FavoriteShape=settings.Formation,DockScale=settings.CardScale,ReduceMotion=settings.ReduceMotion,AutoHideDock=settings.AutoHideBar,
            HideDesktopIconsOnStartup=settings.HideDesktopIconsOnStartup,Hotkey=new(settings.DockKey.Modifiers,settings.DockKey.Key),SettingsHotkey=new(settings.SettingsKey.Modifiers,settings.SettingsKey.Key),
            BackgroundRunningIds=_session.Engine.Current.Entries.Where(e=>e.TrackBackground).Select(e=>e.Id.ToString("N")).ToList(),
            CardLocations=settings.Positions.ToDictionary(e=>e.Key.ToString("N"),e=>new CardLocation(e.Value.X,e.Value.Y)),CardLayoutVersion=4}.Normalize();
    }
}
