using Astria.Desktop.ViewModels;
namespace Astria.Desktop.Services;
internal sealed class IconLoader:IDisposable
{
    private readonly IconCache _cache=new();
    public IconLoader(DesktopSession session){ArgumentNullException.ThrowIfNull(session); }
    public async void Load(AppTile tile)
    {
        if(tile.Entry is not {} entry)return;
        try
        {
            foreach (var source in new[] { entry.CustomIconPath, entry.IconSource, entry.Target }
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var icon = await _cache.Get(source!);
                    if (icon is not null) { tile.Icon = icon; return; }
                }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
                { System.Diagnostics.Trace.WriteLine(error); }
            }
        }
        catch(Exception error) when(error is IOException or ArgumentException or InvalidOperationException){System.Diagnostics.Trace.WriteLine(error);}
    }
    public void Dispose()=>_cache.Dispose();
}
