namespace VCut.Core.Models;

/// <summary>편집할 동영상의 구간(시작~끝). 메인창의 파란색 슬라이더로 설정되는 구간에 해당.</summary>
public readonly struct MediaRange
{
    /// <summary>구간 시작 지점.</summary>
    public TimeSpan Start { get; }

    /// <summary>구간 종료 지점.</summary>
    public TimeSpan End { get; }

    public MediaRange(TimeSpan start, TimeSpan end)
    {
        if (end <= start)
            throw new ArgumentException($"구간 종료({end})는 시작({start})보다 뒤여야 합니다.");
        Start = start;
        End = end;
    }

    /// <summary>구간 길이.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>전체 길이를 그대로 사용하는 구간.</summary>
    public static MediaRange Full(TimeSpan duration) => new(TimeSpan.Zero, duration);

    /// <summary>이 구간이 전체 길이와 사실상 동일한지(±0.1초) 여부.</summary>
    public bool IsFull(TimeSpan total) =>
        Start <= TimeSpan.FromMilliseconds(50) &&
        Math.Abs((End - total).TotalSeconds) < 0.1;

    public override string ToString() => $"{Format(Start)} ~ {Format(End)}";

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.fff");
}
