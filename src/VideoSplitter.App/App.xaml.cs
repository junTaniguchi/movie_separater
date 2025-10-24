using VideoSplitter.Core.Logging;

namespace VideoSplitter.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Instance.Initialize("VideoSplitter");
    }
}
