using System.Diagnostics;
using VCut.Core.Models;

namespace VCut.Core.Operations;

/// <summary>여러 ffmpeg 단계를 하나의 0~1 진행률로 합쳐 보고.</summary>
internal sealed class ProgressScaler : IProgress<ProgressInfo>
{
    private readonly IProgress<ProgressInfo>? _inner;
    private readonly double _start;
    private readonly double _span;
    private readonly TimeSpan _grandTotal;
    private readonly double _baseProcessedSec;
    private Stopwatch? _sw;

    /// <param name="rangeStart">전체 진행률에서 이 단계가 차지하는 시작점(0~1).</param>
    /// <param name="rangeSpan">이 단계가 차지하는 폭(0~1).</param>
    public ProgressScaler(
        IProgress<ProgressInfo>? inner,
        double rangeStart,
        double rangeSpan,
        TimeSpan grandTotal = default,
        double baseProcessedSec = 0)
    {
        _inner = inner;
        _start = rangeStart;
        _span = rangeSpan;
        _grandTotal = grandTotal;
        _baseProcessedSec = baseProcessedSec;
    }

    public void Report(ProgressInfo value)
    {
        if (_inner is null) return;
        double local = value.Fraction ?? 0;
        double global = Math.Clamp(_start + local * _span, 0, 1);

        var processed = _grandTotal > TimeSpan.Zero
            ? TimeSpan.FromSeconds(_baseProcessedSec + value.Processed.TotalSeconds)
            : value.Processed;

        TimeSpan? eta = null;
        if (_grandTotal > TimeSpan.Zero)
        {
            _sw ??= Stopwatch.StartNew();
            var elapsed = _sw.Elapsed.TotalSeconds;
            // 현재 세그먼트에서 진행된 전체 비율(localProgress)을 속도 기준으로 사용.
            // global/elapsed 는 세그먼트마다 _sw가 리셋되어 오차가 크므로 local*_span 사용.
            // ETA = elapsed × (1-global) / localProgress
            double localProgress = local * _span;
            if (localProgress > 0.001 && elapsed > 0.5)
                eta = TimeSpan.FromSeconds(elapsed * (1.0 - global) / localProgress);
            else if (value.Speed > 0.01)
            {
                var remaining = _grandTotal - processed;
                if (remaining > TimeSpan.Zero)
                    eta = TimeSpan.FromSeconds(remaining.TotalSeconds / value.Speed);
            }
        }

        _inner.Report(new ProgressInfo
        {
            Fraction = global,
            Processed = processed,
            Total = _grandTotal > TimeSpan.Zero ? _grandTotal : value.Total,
            Speed = value.Speed,
            Fps = value.Fps,
            Eta = eta ?? value.Eta,
        });
    }
}
