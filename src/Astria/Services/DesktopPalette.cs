namespace Astria.Desktop.Services;

public sealed record DesktopPalette(string Accent, string Glow, string Secondary, string Surface)
{
    public static DesktopPalette For(string theme) => theme switch
    {
        "aurora" => new("#84F3C9", "#3DE0B5", "#685CFF", "#142F32"),
        "neon" => new("#F093FF", "#CA49EF", "#30CCFF", "#2B193C"),
        "abyss" => new("#73D8FF", "#1688CD", "#23D6C7", "#102B40"),
        "gold" => new("#F0CE88", "#D99A42", "#A75C56", "#30291E"),
        "ocean" => new("#8FCFFF", "#559CFF", "#3264B8", "#1C2C42"),
        "mint" => new("#9AE2C7", "#55CFA9", "#337E99", "#1D3237"),
        _ => new("#B8ADFF", "#8E55FF", "#3BA8FF", "#2A344C")
    };
}
