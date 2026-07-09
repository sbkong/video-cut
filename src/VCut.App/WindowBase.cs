using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;

namespace VCut.App;

/// <summary>모든 보조 창의 기반 클래스. 첫 활성화 시 메인 창 중앙에 자동 배치하고, Esc로 닫을 수 있게 함.</summary>
public class WindowBase : Window
{
    private bool _centered;
    private bool _escHooked;

    public WindowBase()
    {
        Activated += OnFirstActivated;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        HookEscape();
        if (_centered) return;
        _centered = true;
        Activated -= OnFirstActivated;
        CenterOnMain();
    }

    private void HookEscape()
    {
        if (_escHooked || Content is not UIElement root) return;
        _escHooked = true;
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnEscapeKeyDown), true);
    }

    private void OnEscapeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) Close();
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
