using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace KawaiiDesktopPets;

public sealed class MacApp : Application
{
    private MacSheepManager? _manager;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            MacNative.HideDockIcon();

            _manager = new MacSheepManager();
            _manager.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
