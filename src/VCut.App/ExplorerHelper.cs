using System.Diagnostics;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace VCut.App;

/// <summary>
/// 결과 폴더를 탐색기로 열 때, 해당 폴더가 이미 열려 있으면 새 창을 띄우지 않고 그 창(및 탭)을 포커싱한다.
/// 열려 있는 창이 없으면 기존처럼 새 탐색기 창을 열고 파일을 선택한다.
/// </summary>
internal static class ExplorerHelper
{
    private const int SW_RESTORE = 9;
    private const int UIA_TabItemControlTypeId = 50019;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    public static void OpenAndSelect(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && TryFocusExistingWindow(dir)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"")
            { UseShellExecute = true });
        }
        catch { /* 탐색기 열기 실패 무시 */ }
    }

    /// <summary>Shell.Application COM으로 열려 있는 탐색기 창을 뒤져 같은 폴더를 찾으면 해당 탭을 활성화하고 포커싱.</summary>
    private static bool TryFocusExistingWindow(string dir)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return false;
            dynamic windows = shell.Windows();

            foreach (dynamic window in windows)
            {
                var folderPath = TryGetFolderPath(window);
                if (folderPath is null) continue;
                if (!PathsEqual(folderPath, dir)) continue;

                var hwnd = new IntPtr((long)window.HWND);
                TrySelectTab(hwnd, Path.GetFileName(dir));
                if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                return true;
            }
        }
        catch { /* COM 사용 불가 시 새 창 열기로 대체 */ }
        return false;
    }

    private static string? TryGetFolderPath(dynamic window)
    {
        try { return (string)window.Document.Folder.Self.Path; }
        catch { return null; }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 탐색기 창이 탭 그룹인 경우, 대상 폴더 이름과 일치하는 탭을 UI Automation으로 선택해 활성화한다.
    /// 탭이 없는(단일) 창이거나 UI Automation을 사용할 수 없으면 조용히 무시한다.
    /// </summary>
    private static void TrySelectTab(IntPtr hwnd, string? tabName)
    {
        if (string.IsNullOrEmpty(tabName)) return;

        try
        {
            var automation = new CUIAutomation();
            var root = automation.ElementFromHandle(hwnd);
            if (root is null) return;

            var condition = automation.CreatePropertyCondition(
                UIA_PropertyIds.UIA_ControlTypePropertyId, UIA_TabItemControlTypeId);
            var tabs = root.FindAll(TreeScope.TreeScope_Descendants, condition);

            for (var i = 0; i < tabs.Length; i++)
            {
                var tab = tabs.GetElement(i);
                if (!string.Equals(tab.CurrentName, tabName, StringComparison.CurrentCultureIgnoreCase)) continue;

                var pattern = tab.GetCurrentPattern(UIA_PatternIds.UIA_SelectionItemPatternId) as IUIAutomationSelectionItemPattern;
                pattern?.Select();
                return;
            }
        }
        catch { /* UI Automation 실패 시 탭 전환 없이 창만 포커싱 */ }
    }
}
