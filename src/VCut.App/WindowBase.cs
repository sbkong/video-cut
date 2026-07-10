using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
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
        root.AddHandler(UIElement.GotFocusEvent is null ? UIElement.KeyDownEvent : UIElement.KeyDownEvent, new KeyEventHandler((_, _) => { }), false);
        if (root is FrameworkElement fe)
        {
            fe.GotFocus += (s, e) => Log($"GotFocus el={Describe(e.OriginalSource as DependencyObject)}");
            fe.LostFocus += (s, e) => Log($"LostFocus el={Describe(e.OriginalSource as DependencyObject)}");
        }
    }

    private static void Log(string msg) => Debug.WriteLine($"[ESC {DateTime.Now:HH:mm:ss.fff}] {msg}");

    private static string Describe(DependencyObject? el) => el switch
    {
        null => "null",
        FrameworkElement { Name.Length: > 0 } f => $"{f.Name}({f.GetType().Name})",
        FrameworkElement f => $"<unnamed {f.GetType().Name}>",
        _ => el.GetType().Name,
    };

    private void OnEscapeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;

        Log($"KeyDown Escape source={Describe(e.OriginalSource as DependencyObject)}");

        // 텍스트 입력 중에는 Esc가 창을 닫지 않고 입력에서 포커스만 빼도록 한다
        // (편집 중 실수로 창이 닫히는 것을 방지). 데스크톱 WinUI 앱에서는 SearchRoot가 있는
        // 오버로드가 필요 — sender가 곧 Esc를 훅한 root(로드된 UIElement)다.
        if (e.OriginalSource is TextBox or PasswordBox or RichEditBox or AutoSuggestBox
            && sender is DependencyObject searchRoot)
        {
            var moved = FocusManager.TryMoveFocus(FocusNavigationDirection.Next,
                new FindNextElementOptions { SearchRoot = searchRoot });
            Log($"  -> TryMoveFocus(Next) result={moved}");
            return;
        }

        Log("  -> Close()");
        Close();
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
