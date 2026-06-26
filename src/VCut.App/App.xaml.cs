using Microsoft.UI.Xaml;

namespace VCut.App;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings.SettingsStore.Load();
        _window = new MainWindow();
        _window.Activate();
    }
}
