using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Astria.Domain;
using Microsoft.Win32;
namespace Astria.Infrastructure;

public sealed class HotkeyBindings(nint window) : IDisposable
{
    private int _next = 200;
    private int _dock, _settings;
    private Preferences? _active;
    public bool IsDock(nint id) => id == _dock;
    public bool IsSettings(nint id) => id == _settings;
    public void Apply(Preferences preferences)
    {
        if (_active?.DockKey == preferences.DockKey && _active?.SettingsKey == preferences.SettingsKey) return;
        if (!preferences.DockKey.IsValid || !preferences.SettingsKey.IsValid || preferences.DockKey == preferences.SettingsKey)
            throw new ArgumentException("两组快捷键须有效且互不相同，不能使用保留键 F12。");
        // Retain each unchanged registration; register replacements before releasing the working pair.
        var dock = _active?.DockKey == preferences.DockKey ? _dock : ++_next;
        var settings = _active?.SettingsKey == preferences.SettingsKey ? _settings : ++_next;
        try
        {
            if (dock != _dock) Register(dock, preferences.DockKey);
            if (settings != _settings) Register(settings, preferences.SettingsKey);
        }
        catch { if (dock != _dock) UnregisterHotKey(window, dock); if (settings != _settings) UnregisterHotKey(window, settings); throw; }
        if (dock != _dock && _dock != 0) UnregisterHotKey(window, _dock);
        if (settings != _settings && _settings != 0) UnregisterHotKey(window, _settings);
        _dock = dock; _settings = settings; _active = preferences;
    }
    private void Register(int id, KeyChord chord)
    { if (!RegisterHotKey(window, id, chord.Modifiers | 0x4000, chord.Key)) throw new Win32Exception(Marshal.GetLastWin32Error(), "快捷键 " + chord.Label + " 无法注册，原设置保留。"); }
    public void Dispose() { UnregisterHotKey(window, _dock); UnregisterHotKey(window, _settings); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}

public sealed class DesktopIcons : IDisposable
{
    private nint _changedWindow;
    private bool? _original, _written;
    public bool? Visible
    {
        get
        {
            var handle = FindIcons(); if (handle == 0) return null;
            var visible = IsWindowVisible(handle);
            if (_changedWindow != 0 && (_changedWindow != handle || _written != visible)) { _changedWindow = 0; _original = null; _written = null; }
            return visible;
        }
    }
    public void SetVisible(bool visible)
    {
        var before = Visible; var handle = FindIcons();
        if (before is null || handle == 0) throw new InvalidOperationException("未找到 Windows 桌面图标视图，请确认资源管理器已启动。");
        if (before == visible) return;
        _original ??= before; _changedWindow = handle; _written = visible;
        ShowWindow(handle, visible ? 8 : 0);
        if (IsWindowVisible(handle) != visible) throw new InvalidOperationException("Windows 未应用桌面图标状态。");
    }
    public void Dispose()
    {
        if (_changedWindow != 0 && IsWindow(_changedWindow) && _original is { } original && _written == IsWindowVisible(_changedWindow))
            ShowWindow(_changedWindow, original ? 8 : 0);
        _changedWindow = 0;
    }
    private static nint FindIcons()
    {
        nint result = 0;
        EnumWindows((window, _) =>
        {
            var className = new StringBuilder(64); GetClassName(window, className, className.Capacity);
            if (className.ToString() is not ("Progman" or "WorkerW")) return true;
            var view = FindWindowEx(window, 0, "SHELLDLL_DefView", null);
            if (view != 0) result = FindWindowEx(view, 0, "SysListView32", null);
            return result == 0;
        }, 0);
        return result;
    }
    private delegate bool Enumerate(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerate callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int size);
}
public static class LoginStartup
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Astria.Next";
    public static bool Enabled(string root)
    { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return string.Equals(key?.GetValue(Name) as string, Command(root), StringComparison.OrdinalIgnoreCase); }
    public static void Set(string root, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        var current = key.GetValue(Name) as string;
        if (current is not null && !string.Equals(current, Command(root), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("已有另一个安装位置的登录启动项，请先在该位置关闭。");
        if (enabled) key.SetValue(Name, Command(root)); else key.DeleteValue(Name, false);
    }
    private static string Command(string root) => $"\"{Environment.ProcessPath}\" --autostart --data-root \"{Path.GetFullPath(root)}\"";
}
