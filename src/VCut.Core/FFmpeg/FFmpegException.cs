namespace VCut.Core.FFmpeg;

/// <summary>ffmpeg/ffprobe 실행 또는 분석 중 발생한 오류.</summary>
public sealed class FFmpegException : Exception
{
    /// <summary>프로세스 종료 코드(있는 경우).</summary>
    public int? ExitCode { get; }

    /// <summary>ffmpeg 표준오류 출력(진단용).</summary>
    public string? StdErr { get; }

    public FFmpegException(string message, int? exitCode = null, string? stdErr = null)
        : base(message)
    {
        ExitCode = exitCode;
        StdErr = stdErr;
    }
}
