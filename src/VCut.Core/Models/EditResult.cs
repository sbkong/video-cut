namespace VCut.Core.Models;

/// <summary>편집 작업 결과.</summary>
public sealed class EditResult
{
    public bool Success { get; init; }

    /// <summary>생성된 출력 파일 경로들(나누기는 여러 개).</summary>
    public IReadOnlyList<string> OutputFiles { get; init; } = [];

    /// <summary>실패 시 오류 메시지.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>ffmpeg가 마지막으로 출력한 표준오류(진단용).</summary>
    public string? FFmpegLog { get; init; }

    public TimeSpan Elapsed { get; init; }

    public static EditResult Ok(IReadOnlyList<string> outputs, TimeSpan elapsed) =>
        new() { Success = true, OutputFiles = outputs, Elapsed = elapsed };

    public static EditResult Ok(string output, TimeSpan elapsed) =>
        new() { Success = true, OutputFiles = [output], Elapsed = elapsed };

    public static EditResult Fail(string message, string? log = null, TimeSpan elapsed = default) =>
        new() { Success = false, ErrorMessage = message, FFmpegLog = log, Elapsed = elapsed };
}
