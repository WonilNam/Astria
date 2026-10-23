using Astria.Desktop.Services;
namespace Astria.AppServices;
public sealed class PresentationLaunch(DesktopSession session)
{
    public PresentationLaunchResult LaunchItem(string id)
    {
        try{var entry=session.Engine.Current.Entries.First(e=>e.Id==Guid.Parse(id));session.Launcher.Open(entry);return new(true,null);}
        catch(Exception error){return new(false,error.Message);}
    }
}
public sealed record PresentationLaunchResult(bool Succeeded,string? Message);
