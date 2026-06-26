namespace VCut.Core.Operations;

/// <summary>출력 파일 경로 생성 규칙. docx의 '저장 옵션 — 원본과 같은 위치/파일명 규칙'에 대응.</summary>
public static class OutputNaming
{
    /// <summary>
    /// 원본과 같은 폴더에, 접미사를 붙인 새 파일 경로를 생성. 확장자는 <paramref name="newExtension"/>으로 교체.
    /// 이미 존재하면 " (1)", " (2)" … 를 덧붙여 충돌을 회피.
    /// </summary>
    public static string Derive(string sourcePath, string suffix, string newExtension, string? targetDir = null)
    {
        var dir = targetDir ?? Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = newExtension.StartsWith('.') ? newExtension : "." + newExtension;

        var baseName = string.IsNullOrEmpty(suffix) ? name : $"{name}_{suffix}";
        var candidate = Path.Combine(dir, baseName + ext);
        int n = 1;
        while (File.Exists(candidate))
            candidate = Path.Combine(dir, $"{baseName} ({n++}){ext}");
        return candidate;
    }

    /// <summary>나누기 등 연번 출력 경로(예: name_part01.mp4).</summary>
    public static string DeriveIndexed(string sourcePath, string suffix, int index, int total, string newExtension, string? targetDir = null)
    {
        var dir = targetDir ?? Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = newExtension.StartsWith('.') ? newExtension : "." + newExtension;
        int width = total.ToString().Length;
        var num = index.ToString().PadLeft(Math.Max(2, width), '0');
        return Path.Combine(dir, $"{name}_{suffix}{num}{ext}");
    }
}
