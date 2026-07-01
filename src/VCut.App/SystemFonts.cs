using System.Runtime.InteropServices;

namespace VCut.App;

/// <summary>Win32 GDI(EnumFontFamiliesEx)로 시스템에 설치된 폰트 패밀리 이름 목록을 가져온다.</summary>
internal static class SystemFonts
{
    private const int LF_FACESIZE = 32;
    private const byte DEFAULT_CHARSET = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = LF_FACESIZE)]
        public string lfFaceName;
    }

    private delegate int EnumFontFamExDelegate(ref LOGFONT lpelfe, IntPtr lpntme, uint fontType, IntPtr lParam);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern int DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int EnumFontFamiliesEx(IntPtr hdc, ref LOGFONT lpLogfont, EnumFontFamExDelegate lpProc, IntPtr lParam, uint dwFlags);

    /// <summary>설치된 폰트 패밀리 이름을 가나다/ABC 정렬로 반환. 세로쓰기용 "@" 접두 패밀리는 제외.</summary>
    public static IReadOnlyList<string> GetInstalledFamilyNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var hdc = CreateDC("DISPLAY", null, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return [];
        try
        {
            var lf = new LOGFONT { lfCharSet = DEFAULT_CHARSET, lfFaceName = "" };
            EnumFontFamiliesEx(hdc, ref lf, Callback, IntPtr.Zero, 0);
        }
        finally { DeleteDC(hdc); }
        return [.. names];

        int Callback(ref LOGFONT lpelfe, IntPtr lpntme, uint fontType, IntPtr lParam)
        {
            if (!string.IsNullOrWhiteSpace(lpelfe.lfFaceName) && lpelfe.lfFaceName[0] != '@')
                names.Add(lpelfe.lfFaceName);
            return 1;
        }
    }
}
