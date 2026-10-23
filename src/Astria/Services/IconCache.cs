using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Astria.Infrastructure;
namespace Astria.Desktop.Services;

internal sealed class IconCache : IDisposable
{
    private readonly Dictionary<string, Task<Bitmap?>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly BlockingCollection<(string, TaskCompletionSource<byte[]?>)> _jobs = new();
    private readonly Thread _worker;
    private readonly List<Bitmap> _images = [];
    private bool _disposed;
    public IconCache()
    {
        _worker = new Thread(() =>
        {
            CoInitializeEx(0, 2);
            try
            {
                foreach (var (path, completion) in _jobs.GetConsumingEnumerable())
                    try { completion.TrySetResult(ShellIcons.Read(path)); } catch { completion.TrySetResult(null); }
            }
            finally { CoUninitialize(); }
        })
        { IsBackground = true, Name = "Astria shell images" };
        _worker.SetApartmentState(ApartmentState.STA); _worker.Start();
    }
    public Task<Bitmap?> Get(string path)
    { if (_cache.TryGetValue(path, out var image)) return image; return _cache[path] = Load(path); }
    private async Task<Bitmap?> Load(string path)
    {
        if(File.Exists(path)&&new[]{".png",".jpg",".jpeg",".bmp",".ico"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))
        {
            using var file=File.OpenRead(path);var image=Bitmap.DecodeToWidth(file,96);_images.Add(image);return image;
        }
        var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jobs.Add((path, completion)); var bytes = await completion.Task; if (bytes is null || _disposed) return null;
        var bitmap = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock()) for (var y = 0; y < 64; y++) Marshal.Copy(bytes, y * 256, buffer.Address + y * buffer.RowBytes, 256);
        _images.Add(bitmap); return bitmap;
    }
    public void Dispose() { _disposed = true; _jobs.CompleteAdding(); foreach (var bitmap in _images) bitmap.Dispose(); }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
