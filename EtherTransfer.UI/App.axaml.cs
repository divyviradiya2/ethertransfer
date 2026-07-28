using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EtherTransfer.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Ensure firewall rules are configured on Windows (triggers UAC if missing)
            // MUST block synchronously here, otherwise MainWindow will bind to ports 
            // before the UAC prompt finishes, causing double firewall popups!
            EtherTransfer.Services.FirewallManager.EnsureFirewallRulesAsync().GetAwaiter().GetResult();

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}