using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Astria.Domain;
using Astria.Infrastructure;
using Astria.Desktop.ViewModels;
using Astria.Desktop.Views;

namespace Astria.Desktop.Services;
internal sealed class DesktopController:IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly DesktopSession _session;
    private readonly WorkspaceViewModel _model;
    private readonly WorkspaceWindow _workspace;
    private readonly DockWindow _dock;
    private readonly IconLoader _icons;
    private readonly DesktopIcons _desktop=new();
    private HotkeyBindings? _keys;
    private NotificationArea? _tray;
    private TrayMenuWindow? _menu;
    private readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromSeconds(3)};
    private bool _disposed,_refreshing;
    public DesktopController(IClassicDesktopStyleApplicationLifetime lifetime,DesktopSession session)
    {
        _lifetime=lifetime;_session=session;
        if(!session.Preferences.Current.DefaultShellLocationsSeeded)
        {
            session.Import([new(){Source=Astria.Core.Models.ShellLocations.ThisComputer},new(){Source=Astria.Core.Models.ShellLocations.RecycleBin}]);
            session.Preferences.Save(session.Preferences.Current with {DefaultShellLocationsSeeded=true});
        }
        _model=new(session);
        _model.ReadDesktopIcons=()=>_desktop.Visible;
        _model.WriteDesktopIcons=visible=>{try{_desktop.SetVisible(visible);return null;}catch(Exception error){return error.Message;}};
        _model.RegisterHotkey=key=>Register(_session.Preferences.Current with {Hotkey=key});
        _model.RegisterSettingsHotkey=key=>Register(_session.Preferences.Current with {SettingsHotkey=key});
        _workspace=new(session,_model,Activate);_dock=new(session,_model,ShowWorkspace,Activate);_icons=new(session);
        var icon=new WindowIcon(AssetLoader.Open(new Uri("avares://Astria/Assets/Astria.ico")));_workspace.Icon=icon;_dock.Icon=icon;
        _model.PreferencesChanged+=PreferencesChanged;_model.TilesChanged+=TilesChanged;
        Win32Properties.AddWndProcHookCallback(_dock,WindowProc);_dock.Opened+=Opened;
        lifetime.MainWindow=_dock;_dock.Show();_dock.Hide();
        _model.ApplyDesktopIconPreference();ApplyPreferences();LoadIcons();
        _timer.Tick+=async(_,_)=>await RefreshRunning();_timer.Start();_=RefreshRunning();
    }
    private void Opened(object? sender,EventArgs args)
    {
        if(_keys is not null||_dock.TryGetPlatformHandle()?.Handle is not {} handle)return;
        _keys=new(handle);_tray=new(handle,Path.Combine(AppContext.BaseDirectory,"Astria.ico"));
        if(Register(_session.Preferences.Current) is {} error)_model.Status=error;
    }
    private string? Register(DesktopPreferences preferences)
    {
        if(_keys is null)return "窗口尚未初始化，请稍后重试。";
        try{_keys.Apply(_session.Engine.Current.Settings with {DockKey=new(preferences.Hotkey.Modifiers,preferences.Hotkey.VirtualKey),SettingsKey=new(preferences.SettingsHotkey.Modifiers,preferences.SettingsHotkey.VirtualKey)});return null;}
        catch(Exception error){return error.Message;}
    }
    private nint WindowProc(nint window,uint message,nint wParam,nint lParam,ref bool handled)
    {
        if(message==0x0312&&_keys is not null)
        {
            if(_keys.IsDock(wParam)){ToggleDock();handled=true;}else if(_keys.IsSettings(wParam)){ToggleSettings();handled=true;}
        }
        if(message==NotificationArea.Message)
        {
            var action=(uint)lParam&0xFFFF;
            if(action is 0x205 or 0x7B)
            {
                _menu?.Close();_menu=new(ShowDock,()=>ShowManager(false),()=>ShowManager(true),()=>_lifetime.Shutdown());_menu.OpenAtPointer();handled=true;
            }
            else if(action is 0x202 or 0x203){ShowDock();handled=true;}
        }
        if(_tray is not null&&message==_tray.RestartMessage)_tray.Show();
        return 0;
    }
    public void ShowDock(){_workspace.Hide();_model.RefreshDesktopIcons();_dock.Reveal();_=RefreshRunning();}
    public void ToggleDock(){if(_dock.IsVisible&&_dock.IsActive)_dock.Hide();else ShowDock();}
    public void ShowManager(bool settings)=>ShowWorkspace(settings?WorkspaceSection.Settings:WorkspaceSection.Library);
    public void ToggleSettings()
    {if(_workspace.IsVisible&&_workspace.IsActive&&_workspace.WindowState!=WindowState.Minimized&&_model.IsSettings)_workspace.Hide();else ShowManager(true);}
    private void ShowWorkspace(WorkspaceSection section){_dock.Hide();_workspace.ShowSection(section);_=RefreshRunning();}
    private void PreferencesChanged(object? sender,EventArgs args)=>ApplyPreferences();
    private void ApplyPreferences()
    {
        if(_keys is not null&&Register(_session.Preferences.Current) is {} error)_model.Status=error;
        _workspace.ApplyTheme();_workspace.TransparencyLevelHint=_model.UseAcrylic?[WindowTransparencyLevel.AcrylicBlur,WindowTransparencyLevel.None]:[WindowTransparencyLevel.None];
        Avalonia.Application.Current!.Resources["Accent"]=new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(DesktopPalette.For(_model.Accent).Accent));
        foreach(var theme in Avalonia.Application.Current.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>())
        {
            if(!theme.Palettes.TryGetValue(Avalonia.Styling.ThemeVariant.Dark,out var palette))
                theme.Palettes[Avalonia.Styling.ThemeVariant.Dark]=palette=new Avalonia.Themes.Fluent.ColorPaletteResources();
            palette.Accent=Avalonia.Media.Color.Parse(DesktopPalette.For(_model.Accent).Accent);
        }
        _dock.ApplyPreferences();
    }
    private void TilesChanged(object? sender,EventArgs args)=>LoadIcons();
    private void LoadIcons(){foreach(var tile in _model.VisibleApps.Concat(_model.DockApps).Distinct())if(tile.Icon is null)_icons.Load(tile);}
    private async Task RefreshRunning()
    {
        if(_disposed||_refreshing)return;_refreshing=true;
        try
        {
            var background=_session.Preferences.Current.BackgroundRunningIds.Count>0;
            var snapshot=await Task.Run(()=>
            {
                _session.Services.Running.Reconcile();
                return (Windows:DesktopRunningState.CaptureWindows(_session.Services.Running.LastWindows),Background:background?DesktopRunningState.CaptureBackgroundProcesses():new Dictionary<string,string?>());
            });
            if(!_disposed){_model.RefreshRunning(snapshot.Windows,snapshot.Background);if(_dock.IsVisible)_model.RefreshDesktopIcons();}
        }
        catch(Exception error){if(!_disposed)_model.Status=error.Message;}
        finally{_refreshing=false;}
    }
    private void Activate(AppTile tile,bool fromDock)
    {
        if(tile.IsFolder){_model.OpenFolder(tile.Id);ShowWorkspace(WorkspaceSection.Library);return;}
        try
        {
            var entry=_session.Engine.Current.Entries.First(e=>e.Id==Guid.Parse(tile.Id));var windows=_session.Services.Running.LastWindows.Where(w=>Astria.Application.RunningIdentity.Matches(entry,new Astria.Application.AppWindow(w.Handle,w.Title,w.ImagePath??"",w.EffectiveAumid??""))).ToArray();
            if(windows.Length>1)
            {
                var menu=new ContextMenu();foreach(var candidate in windows)
                {var item=new MenuItem{Header=candidate.Title};item.Click+=(_,_)=>{_session.Launcher.Activate(candidate.Handle);_dock.Hide();_workspace.Hide();};menu.Items.Add(item);}
                menu.Open(fromDock?_dock:_workspace);return;
            }
            if(fromDock&&windows.Length==1&&_session.Launcher.MinimizeIfForeground(windows[0].Handle))
            {if(_session.Preferences.Current.ImmersiveLaunchpad)_dock.Hide();return;}
            _session.Engine.Launch(entry,_session.Services.Running.LastWindows.Select(w=>new Astria.Application.AppWindow(w.Handle,w.Title,w.ImagePath??"",w.EffectiveAumid??"")).ToArray());
            if(fromDock){if(_session.Preferences.Current.ImmersiveLaunchpad)_dock.Hide();}else _workspace.Hide();
        }
        catch(Exception error){ShowWorkspace(WorkspaceSection.Library);_model.Status=error.Message;}
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_timer.Stop();_keys?.Dispose();_desktop.Dispose();_tray?.Dispose();_menu?.Close();
        _model.PreferencesChanged-=PreferencesChanged;_model.TilesChanged-=TilesChanged;
        Win32Properties.RemoveWndProcHookCallback(_dock,WindowProc);_workspace.Shutdown();_dock.Shutdown();_icons.Dispose();_session.Dispose();
    }
}
