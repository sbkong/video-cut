using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace VCut.App;

/// <summary>모든 보조 창의 기반 클래스. 첫 활성화 시 메인 창 중앙에 자동 배치하고, Esc로 닫을 수 있게 함.</summary>
public class WindowBase : Window
{
    private bool _centered;
    private bool _escHooked;

    public WindowBase()
    {
        Activated += OnActivated;
    }

    // 중앙 배치는 첫 활성화 때 1회. Esc 후킹은 Content가 준비돼야 하므로,
    // 둘 다 끝날 때까지 Activated 구독을 유지한다(첫 활성화에 Content가 없어도 Esc를 놓치지 않도록).
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        HookEscape();

        if (!_centered)
        {
            _centered = true;
            CenterOnMain();
        }

        if (_escHooked)
            Activated -= OnActivated;
    }

    private void HookEscape()
    {
        if (_escHooked || Content is not UIElement root) return;
        _escHooked = true;
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnEscapeKeyDown), true);
    }

    private void OnEscapeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;

        // 텍스트 입력 중에는 Esc가 창을 닫지 않고 입력에서 포커스만 빼도록 한다
        // (편집 중 실수로 창이 닫히는 것을 방지). 데스크톱 WinUI 앱에서는 SearchRoot가 있는
        // 오버로드가 필요 — sender가 곧 Esc를 훅한 root(로드된 UIElement)다.
        if (e.OriginalSource is TextBox or PasswordBox or RichEditBox or AutoSuggestBox
            && sender is DependencyObject searchRoot)
        {
            FocusManager.TryMoveFocus(FocusNavigationDirection.Next,
                new FindNextElementOptions { SearchRoot = searchRoot });
            return;
        }

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

    // ════════ 최소 창 크기 (WinUI 3엔 API가 없어 WM_GETMINMAXINFO를 서브클래싱으로 처리) ════════

    private delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);
    private WndProcDelegate? _wndProc;
    private IntPtr _originalWndProc;
    private int _minW, _minH;   // 논리 px (DPI 미적용값). WndProc에서 DPI 배율을 곱한다.

    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr val);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    /// <summary>이 창의 최소 크기를 지정(논리 px). 생성자에서 호출.</summary>
    protected void SetMinSize(int width, int height)
    {
        _minW = width;
        _minH = height;
        if (_wndProc is not null) return;   // 서브클래싱은 1회만 설치
        var hwnd = WindowNative.GetWindowHandle(this);
        _wndProc = WndProc;
        _originalWndProc = SetWindowLongPtr(hwnd, -4, Marshal.GetFunctionPointerForDelegate(_wndProc)); // GWLP_WNDPROC
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == 0x0024 && _minW > 0)   // WM_GETMINMAXINFO
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            double scale = GetDpiForWindow(hwnd) / 96.0;
            mmi.ptMinTrackSize.x = (int)(_minW * scale);
            mmi.ptMinTrackSize.y = (int)(_minH * scale);
            Marshal.StructureToPtr(mmi, lParam, true);
        }
        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
    }
}
