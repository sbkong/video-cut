using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VCut.Core.FFmpeg;

/// <summary>
/// ffmpeg/ffprobe 실행 파일 위치를 결정. 우선순위:
/// (1) 명시적으로 지정된 경로 → (2) 앱 폴더에 동봉된 바이너리 → (3) 시스템 PATH.
/// </summary>
public sealed class FFmpegLocator
{
    private static readonly string ExeSuffix =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

    public string FFmpegPath { get; }
    public string FFprobePath { get; }

    private FFmpegLocator(string ffmpeg, string ffprobe)
    {
        FFmpegPath = ffmpeg;
        FFprobePath = ffprobe;
    }

    /// <summary>
    /// 위치를 탐색해 로케이터를 생성. <paramref name="explicitDirectory"/>가 주어지면
    /// 그 폴더를 먼저 확인하고, 없으면 앱 베이스 디렉터리, 마지막으로 PATH를 사용.
    /// </summary>
    public static FFmpegLocator Create(string? explicitDirectory = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
            candidates.Add(explicitDirectory!);
        candidates.Add(AppContext.BaseDirectory);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg"));

        foreach (var dir in candidates)
        {
            var ffmpeg = Path.Combine(dir, "ffmpeg" + ExeSuffix);
            var ffprobe = Path.Combine(dir, "ffprobe" + ExeSuffix);
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                return new FFmpegLocator(ffmpeg, ffprobe);
        }

        // PATH 폴백 — 이름만으로 실행(OS가 PATH에서 해석).
        if (ExistsOnPath("ffmpeg" + ExeSuffix) && ExistsOnPath("ffprobe" + ExeSuffix))
            return new FFmpegLocator("ffmpeg" + ExeSuffix, "ffprobe" + ExeSuffix);

        throw new FFmpegException(
            "ffmpeg/ffprobe 실행 파일을 찾을 수 없습니다. 앱 폴더에 동봉하거나 시스템 PATH에 추가하세요.");
    }

    /// <summary>탐색 실패 시 예외 대신 null 반환.</summary>
    public static FFmpegLocator? TryCreate(string? explicitDirectory = null)
    {
        try { return Create(explicitDirectory); }
        catch (FFmpegException) { return null; }
    }

    private static bool ExistsOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                if (File.Exists(Path.Combine(dir.Trim(), fileName)))
                    return true;
            }
            catch { /* 잘못된 PATH 항목 무시 */ }
        }
        return false;
    }

    /// <summary>ffmpeg 버전 문자열을 반환(진단용).</summary>
    public string GetVersion()
    {
        var psi = new ProcessStartInfo(FFmpegPath, "-version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new FFmpegException("ffmpeg 실행 실패");
        var firstLine = p.StandardOutput.ReadLine() ?? "";
        p.WaitForExit();
        return firstLine;
    }
}
