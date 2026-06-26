namespace VCut.Core.Models;

/// <summary>편집 작업 진행 상황. ffmpeg의 -progress 출력에서 파싱됨.</summary>
public readonly struct ProgressInfo
{
    /// <summary>0.0 ~ 1.0 진행률. 전체 길이를 모르면 null.</summary>
    public double? Fraction { get; init; }

    /// <summary>현재까지 처리된 출력 시점.</summary>
    public TimeSpan Processed { get; init; }

    /// <summary>대상 전체 길이.</summary>
    public TimeSpan Total { get; init; }

    /// <summary>인코딩 속도 배율(예: 3.2x).</summary>
    public double Speed { get; init; }

    /// <summary>초당 처리 프레임 수.</summary>
    public double Fps { get; init; }

    /// <summary>예상 남은 시간. 계산 불가 시 null.</summary>
    public TimeSpan? Eta { get; init; }

    public int Percent => Fraction is { } f ? (int)Math.Round(Math.Clamp(f, 0, 1) * 100) : 0;
}
