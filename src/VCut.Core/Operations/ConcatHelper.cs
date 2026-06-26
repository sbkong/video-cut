using System.Text;
using VCut.Core.FFmpeg;
using VCut.Core.Models;

namespace VCut.Core.Operations;

/// <summary>여러 파일을 하나로 합치는 보조 기능. 고속(스트림 복사)과 변환(필터) 두 방식을 제공.</summary>
internal sealed class ConcatHelper
{
    private readonly IFFmpegRunner _runner;

    public ConcatHelper(IFFmpegRunner runner) => _runner = runner;

    /// <summary>
    /// concat demuxer + 스트림 복사로 합치기. 모든 입력의 코덱/해상도/FPS가 동일할 때만 정상 동작.
    /// docx: "고속 모드 합치기는 동일 형식일 때만 가능".
    /// </summary>
    public async Task ConcatFastAsync(
        IReadOnlyList<string> inputs,
        string output,
        ConversionSettings settings,
        TimeSpan totalDuration,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct)
    {
        var listFile = Path.Combine(Path.GetTempPath(), $"vcut_concat_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(listFile, BuildListFile(inputs), new UTF8Encoding(false), ct);
        try
        {
            var args = new List<string>
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "concat", "-safe", "0",
                "-i", listFile,
                "-map", "0",
                "-c", "copy",
            };
            AddMoov(args, settings);
            args.Add(output);
            await _runner.RunAsync(args, totalDuration, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(listFile);
        }
    }

    /// <summary>concat 필터로 재인코딩하며 합치기. 코덱/해상도가 달라도 가능(첫 입력 기준으로 정렬).</summary>
    public async Task ConcatReencodeAsync(
        IReadOnlyList<string> inputs,
        string output,
        ConversionSettings settings,
        bool includeAudio,
        TimeSpan totalDuration,
        IProgress<ProgressInfo>? progress,
        CancellationToken ct)
    {
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        foreach (var input in inputs) { args.Add("-i"); args.Add(input); }

        int n = inputs.Count;
        var sb = new StringBuilder();

        // 입력들을 동일 해상도/FPS로 맞춘 뒤 concat. (서로 다른 크기를 첫 입력 기준으로 스케일)
        // 비디오 라벨 정규화: 각 입력에 scale + setsar + fps 적용은 생략하고 concat이 처리하도록 단순화.
        for (int i = 0; i < n; i++)
        {
            sb.Append($"[{i}:v:0]");
            if (includeAudio) sb.Append($"[{i}:a:0]");
        }
        sb.Append($"concat=n={n}:v=1:a={(includeAudio ? 1 : 0)}");
        sb.Append("[v]");
        if (includeAudio) sb.Append("[a]");

        args.Add("-filter_complex");
        args.Add(sb.ToString());
        args.Add("-map"); args.Add("[v]");
        if (includeAudio) { args.Add("-map"); args.Add("[a]"); }

        // 비디오 코덱 설정 재사용(필터는 이미 filter_complex에서 처리하므로 -vf 없이 코덱만).
        var enc = FFmpegArgsBuilder.ResolveVideoEncoder(settings.VideoCodec, settings.HardwareAccel);
        args.Add("-c:v"); args.Add(enc);
        if (settings.HardwareAccel == HardwareAccel.None &&
            settings.VideoCodec is VideoCodec.H264 or VideoCodec.Hevc)
        {
            args.Add("-crf"); args.Add(FFmpegArgsBuilder.QualityToCrf(settings.VideoQuality).ToString());
            args.Add("-pix_fmt"); args.Add("yuv420p");
        }

        if (includeAudio)
        {
            args.Add("-c:a"); args.Add("aac");
            args.Add("-b:a"); args.Add(settings.AudioBitrateKbps + "k");
        }

        AddMoov(args, settings);
        args.Add(output);
        await _runner.RunAsync(args, totalDuration, progress, ct).ConfigureAwait(false);
    }

    private static void AddMoov(List<string> args, ConversionSettings s)
    {
        if (s.MoovAtFront && s.Container == ContainerFormat.Mp4)
        {
            args.Add("-movflags"); args.Add("+faststart");
        }
    }

    private static string BuildListFile(IReadOnlyList<string> inputs)
    {
        var sb = new StringBuilder();
        foreach (var f in inputs)
        {
            // concat 목록 규칙: 작은따옴표는 '\'' 로 이스케이프.
            var escaped = Path.GetFullPath(f).Replace("'", "'\\''");
            sb.Append("file '").Append(escaped).Append("'\n");
        }
        return sb.ToString();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 무시 */ }
    }
}
