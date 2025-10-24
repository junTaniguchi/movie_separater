using System.Windows;
using VideoSplitter.Core.Logging;

namespace VideoSplitter.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Logger.Instance.Initialize("VideoSplitter");
    }
}
