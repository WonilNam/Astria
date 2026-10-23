using System.Globalization;
using System.Text;
using Astria.Domain;
namespace Astria.Application;

public sealed class SearchIndex
{
    private static readonly Lazy<Dictionary<int, string>> Mandarin = new(ReadMandarin);
    private readonly Dictionary<Guid, (string Name, string Pinyin, string Initials)> _keys = [];
    public IEnumerable<LaunchEntry> Find(IEnumerable<LaunchEntry> entries, string query)
    {
        var q = Fold(query);
        return entries.Select(e => (Entry: e, Score: new[]{Score(e,q)}.Concat((e.SearchAliases??[]).Select(alias=>Score(e with {Name=alias},q))).Where(score=>score>=0).DefaultIfEmpty(-1).Min())).Where(p => p.Score >= 0).OrderBy(p => p.Score).Select(p => p.Entry);
    }
    private int Score(LaunchEntry entry, string query)
    {
        if (query.Length == 0) return 0;
        if (!_keys.TryGetValue(entry.Id, out var key) || key.Name != Fold(entry.Name))
        {
            var full = new StringBuilder(); var initials = new StringBuilder();
            foreach (var rune in entry.Name.EnumerateRunes())
            {
                var syllable = Mandarin.Value.GetValueOrDefault(rune.Value, Fold(rune.ToString()));
                full.Append(syllable); if (syllable.Length > 0) initials.Append(syllable[0]);
            }
            _keys[entry.Id] = key = (Fold(entry.Name), full.ToString(), initials.ToString());
        }
        if (key.Name == query) return 0;
        if (key.Name.StartsWith(query, StringComparison.Ordinal)) return 1;
        if (key.Name.Contains(query, StringComparison.Ordinal)) return 2;
        if (key.Pinyin.Contains(query, StringComparison.Ordinal) || key.Initials.StartsWith(query, StringComparison.Ordinal)) return 3;
        return -1;
    }
    private static string Fold(string text) => string.Concat(text.Normalize(NormalizationForm.FormD).Where(c => !char.IsWhiteSpace(c)
        && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    private static Dictionary<int, string> ReadMandarin()
    {
        var result = new Dictionary<int, string>();
        using var input = typeof(SearchIndex).Assembly.GetManifestResourceStream("Astria.Application.Data.mandarin.tsv")!;
        using var reader = new StreamReader(input);
        while (reader.ReadLine() is { } line)
        {
            var cells = line.Split('\t');
            if (cells.Length >= 2 && cells[0].StartsWith("U+", StringComparison.Ordinal)
                && int.TryParse(cells[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp)) result[cp] = Fold(cells[1]);
        }
        return result;
    }
}
