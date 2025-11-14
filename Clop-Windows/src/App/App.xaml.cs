using System.Windows;
using ClopWindows.Core.Settings;

namespace ClopWindows.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SettingsHost.EnsureInitialized();
    }
}
