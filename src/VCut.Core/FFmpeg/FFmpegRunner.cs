using System.Diagnostics;
using System.Globalization;
using System.Text;
using VCut.Core.Models;

namespace VCut.Core.FFmpeg;

/// <summary>ffmpeg 프로세스를 실행하고 진행률을 파싱하는 러너.</summary>
public interface IFFmpegRunner
{
    /// <summary>
    /// 인자 목록으로 ffmpeg를 실행. <paramref name="totalDuration"/>이 주어지면 진행률(0~1)을 계산.
    /// </summary>
    /// <returns>ffmpeg가 마지막에 출력한 표준오류 텍스트(진단용).</returns>
    Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? totalDuration = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class FFmpegRunner : IFFmpegRunner
{
    private readonly FFmpegLocator _locator;

    public FFmpegRunner(FFmpegLocator locator) => _locator = locator;

    public async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? totalDuration = null,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 진행률을 stdout으로 받기 위해 -progress pipe:1 -nostats를 선행 추가.
        var args = new List<string> { "-progress", "pipe:1", "-nostats" };
        args.AddRange(arguments);

        var psi = new ProcessStartInfo(_locator.FFmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        // stderr는 링버퍼처럼 마지막 부분만 보관(오류 진단용, 메모리 폭주 방지).
        var stderrTail = new StderrTail(capacity: 64);

        // 진행률 누적 상태.
        double lastSpeed = 0, lastFps = 0;
        TimeSpan lastProcessed = TimeSpan.Zero;
        // ETA를 순간 속도가 아닌 경과 시간 기반 누적 평균으로 계산하기 위한 스톱워치.
        // 첫 progress 보고 시 시작(ffmpeg 초기화 시간 제외).
        Stopwatch? etaSw = null;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderrTail.Add(e.Data);
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null || progress is null) return;
            var line = e.Data;
            int eq = line.IndexOf('=');
            if (eq <= 0) return;
            var key = line.AsSpan(0, eq);
            var val = line.AsSpan(eq + 1);

            if (key.SequenceEqual("out_time_us") || key.SequenceEqual("out_time_ms"))
            {
                // out_time_us는 마이크로초. (구버전 out_time_ms도 실제로는 마이크로초 단위)
                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0)
                    lastProcessed = TimeSpan.FromMicroseconds(us);
            }
            else if (key.SequenceEqual("speed"))
            {
                var s = val.TrimEnd('x').Trim();
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sp))
                    lastSpeed = sp;
            }
            else if (key.SequenceEqual("fps"))
            {
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    lastFps = f;
            }
            else if (key.SequenceEqual("progress"))
            {
                // 한 묶음(progress=continue|end)이 끝나는 시점에 콜백 발행.
                etaSw ??= Stopwatch.StartNew();

                double? fraction = null;
                TimeSpan? eta = null;
                if (totalDuration is { } total && total > TimeSpan.Zero)
                {
                    fraction = Math.Clamp(lastProcessed.TotalSeconds / total.TotalSeconds, 0, 1);
                    // 경과 시간 기반 누적 평균 속도로 ETA 계산.
                    // ETA = elapsed × (1 - f) / f  →  순간 속도 노이즈 없이 안정적.
                    var elapsed = etaSw.Elapsed.TotalSeconds;
                    if (fraction > 0.001 && elapsed > 0.5)
                        eta = TimeSpan.FromSeconds(elapsed * (1.0 - fraction.Value) / fraction.Value);
                    else if (lastSpeed > 0.01)
                    {
                        // 시작 직후 경과 시간이 짧을 때만 순간 속도로 폴백.
                        var remainingOut = total - lastProcessed;
                        if (remainingOut > TimeSpan.Zero)
                            eta = TimeSpan.FromSeconds(remainingOut.TotalSeconds / lastSpeed);
                    }
                }
                progress.Report(new ProgressInfo
                {
                    Fraction = fraction,
                    Processed = lastProcessed,
                    Total = totalDuration ?? TimeSpan.Zero,
                    Speed = lastSpeed,
                    Fps = lastFps,
                    Eta = eta,
                });
            }
        };

        try
        {
            if (!process.Start())
                throw new FFmpegException("ffmpeg 프로세스를 시작할 수 없습니다.");
        }
        catch (Exception ex) when (ex is not FFmpegException)
        {
            throw new FFmpegException($"ffmpeg 실행 실패: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        // 비동기 출력 읽기가 완전히 flush되도록 보장.
        process.WaitForExit();

        var tail = stderrTail.ToString();
        if (process.ExitCode != 0)
            throw new FFmpegException(
                $"ffmpeg가 코드 {process.ExitCode}(으)로 종료되었습니다.",
                process.ExitCode, tail);

        return tail;
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* 이미 종료됨 */ }
    }

    /// <summary>최근 N줄만 보관하는 순환 버퍼.</summary>
    private sealed class StderrTail(int capacity)
    {
        private readonly Queue<string> _lines = new();
        private readonly object _lock = new();

        public void Add(string line)
        {
            lock (_lock)
            {
                _lines.Enqueue(line);
                while (_lines.Count > capacity) _lines.Dequeue();
            }
        }

        public override string ToString()
        {
            lock (_lock) return string.Join('\n', _lines);
        }
    }
}
