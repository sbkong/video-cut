using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VCut.App;

/// <summary>
/// 결과 폴더를 탐색기로 열 때, 해당 폴더가 이미 열려 있으면 새 창을 띄우지 않고 그 창을 포커싱한다.
/// 열려 있는 창이 없으면 기존처럼 새 탐색기 창을 열고 파일을 선택한다.
/// </summary>
internal static class ExplorerHelper
{
    private const int SW_RESTORE = 9;

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

    /// <summary>Shell.Application COM으로 열려 있는 탐색기 창을 뒤져 같은 폴더를 찾으면 포커싱.</summary>
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
}
