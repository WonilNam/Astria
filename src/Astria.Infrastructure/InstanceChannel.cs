using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
namespace Astria.Infrastructure;

public sealed class InstanceChannel : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;
    public bool Primary { get; }
    public event Action<string>? Command;
    public static string Identity(string root)
    {
        var identity = Encoding.UTF8.GetBytes(Environment.UserName + "|" + System.Diagnostics.Process.GetCurrentProcess().SessionId + "|" + Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant());
        return "Astria.Next." + Convert.ToHexString(SHA256.HashData(identity));
    }
    public InstanceChannel(string root)
    {
        _name = Identity(root);
        _mutex = new Mutex(true, "Local\\" + _name, out var created); Primary = created;
    }
    public void Start()
    {
        if (!Primary || _listener is not null) return;
        _listener = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true);
                    var line = await reader.ReadLineAsync(_stop.Token);
                    if (line is "dock" or "settings" or "exit") Command?.Invoke(line);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { if (!_stop.IsCancellationRequested) await Task.Delay(100, _stop.Token); }
            }
        });
    }
    public async Task Send(string command)
    {
        await using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000);
        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true }; await writer.WriteLineAsync(command);
    }
    public void Dispose()
    {
        _stop.Cancel(); if (Primary) _mutex.ReleaseMutex(); _mutex.Dispose();
    }
}
