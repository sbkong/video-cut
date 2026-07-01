using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace VCut.App;

/// <summary>모든 보조 창의 기반 클래스. 첫 활성화 시 메인 창 중앙에 자동 배치.</summary>
public class WindowBase : Window
{
    private bool _centered;

    public WindowBase()
    {
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_centered) return;
        _centered = true;
        Activated -= OnFirstActivated;
        CenterOnMain();
    }

    private void CenterOnMain()
    {
        var main = App.MainWindow;
        if (main?.AppWindow is null || AppWindow is null) return;

        var owner = main.AppWindow;
        var x = owner.Position.X + (owner.Size.Width  - AppWindow.Size.Width)  / 2;
        var y = owner.Position.Y + (owner.Size.Height - AppWindow.Size.Height) / 2;
        AppWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));
    }
}
