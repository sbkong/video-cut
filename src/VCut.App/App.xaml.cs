using Microsoft.UI.Xaml;
using VCut.App.Locale;

namespace VCut.App;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Settings.SettingsStore.Load();
        Loc.Load(Settings.SettingsStore.Current.Language);
        FontService.ApplyAtStartup(Settings.SettingsStore.Current);
        MainWindow = new MainWindow();
        // 저장된 테마를 메인 창 콘텐츠에 즉시 적용 (기본값이 Dark이므로 Light일 때만 실질적으로 바뀜)
        if (MainWindow.Content is FrameworkElement root)
        {
            ThemeService.Apply(Settings.SettingsStore.Current.Theme, root);
            FontService.ApplyToRoot(root, Settings.SettingsStore.Current);
        }
        MainWindow.Activate();
    }
}
