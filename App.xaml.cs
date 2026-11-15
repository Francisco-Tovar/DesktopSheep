using System.Windows;

namespace KawaiiDesktopPets;

public partial class App : System.Windows.Application
{
    private SheepManager? _manager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _manager = new SheepManager();
        _manager.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _manager = null;
        base.OnExit(e);
    }
}
