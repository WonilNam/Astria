using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Astria.Infrastructure;
public sealed class NotificationArea:IDisposable
{
    public const uint Message=0x8001;
    public uint RestartMessage{get;}=RegisterWindowMessage("TaskbarCreated");
    private NotificationData _data;
    public NotificationArea(nint window,string icon)
    {
        var handle=LoadImage(0,icon,1,32,32,0x10);
        if(handle==0)throw new Win32Exception(Marshal.GetLastWin32Error());
        _data=new(){Size=(uint)Marshal.SizeOf<NotificationData>(),Window=window,Id=1,Flags=7,Callback=Message,Icon=handle,Tip="星屿 Astria",Info="",Title=""};
        Show();
    }
    public void Show()=>ShellNotifyIcon(0,ref _data);
    public void Dispose(){ShellNotifyIcon(2,ref _data);if(_data.Icon!=0)DestroyIcon(_data.Icon);_data.Icon=0;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct NotificationData
    {
        public uint Size;public nint Window;public uint Id,Flags,Callback;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public nint Balloon;
    }
    [DllImport("shell32.dll",EntryPoint="Shell_NotifyIconW",CharSet=CharSet.Unicode)]private static extern bool ShellNotifyIcon(uint command,ref NotificationData data);
    [DllImport("user32.dll",EntryPoint="LoadImageW",CharSet=CharSet.Unicode,SetLastError=true)]private static extern nint LoadImage(nint instance,string file,uint type,int x,int y,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll",EntryPoint="RegisterWindowMessageW",CharSet=CharSet.Unicode)]private static extern uint RegisterWindowMessage(string text);
}
