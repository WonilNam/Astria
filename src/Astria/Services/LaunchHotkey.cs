using Avalonia.Input;

namespace Astria.Desktop.Services;

public sealed record LaunchHotkey(uint Modifiers = 3, uint VirtualKey = 32)
{
    public bool IsValid => Modifiers is > 0 and < 16 &&
        (VirtualKey is >= 48 and <= 57 or >= 65 and <= 90 or >= 112 and <= 135 or 32 or 13 or 9);
    public string Label => string.Join(" + ", new[]
    {
        (Modifiers & 2) != 0 ? "Ctrl" : null, (Modifiers & 1) != 0 ? "Alt" : null,
        (Modifiers & 4) != 0 ? "Shift" : null, (Modifiers & 8) != 0 ? "Win" : null,
        VirtualKey switch { 32 => "Space", 13 => "Enter", 9 => "Tab", >= 112 and <= 135 => "F" + (VirtualKey - 111), _ => ((char)VirtualKey).ToString() }
    }.OfType<string>());

    public static LaunchHotkey? Capture(Key key, KeyModifiers modifiers)
    {
        uint code = key switch
        {
            >= Key.A and <= Key.Z => (uint)(key - Key.A + 65),
            >= Key.D0 and <= Key.D9 => (uint)(key - Key.D0 + 48),
            >= Key.F1 and <= Key.F24 => (uint)(key - Key.F1 + 112),
            Key.Space => 32, Key.Enter => 13, Key.Tab => 9, _ => 0
        };
        uint flags = (modifiers.HasFlag(KeyModifiers.Control) ? 2u : 0) |
            (modifiers.HasFlag(KeyModifiers.Alt) ? 1u : 0) |
            (modifiers.HasFlag(KeyModifiers.Shift) ? 4u : 0) |
            (modifiers.HasFlag(KeyModifiers.Meta) ? 8u : 0);
        var result = new LaunchHotkey(flags, code);
        return result.IsValid ? result : null;
    }
}
