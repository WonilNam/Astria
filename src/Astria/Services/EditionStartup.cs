namespace Astria.Desktop.Services;
internal static class EditionStartup
{
    public static bool IsEnabled(string root)=>Astria.Infrastructure.LoginStartup.Enabled(root);
    public static void SetEnabled(string root,bool enabled)=>Astria.Infrastructure.LoginStartup.Set(root,enabled);
}
