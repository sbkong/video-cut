using CommunityToolkit.Mvvm.ComponentModel;

namespace VCut.App.ViewModels;

/// <summary>'자르기 구간 목록'의 항목 하나 — 파일 + 편집 구간(시작/끝).</summary>
public sealed partial class ClipSegment : ObservableObject
{
    public string FilePath { get; }

    /// <summary>원본 전체 길이.</summary>
    public TimeSpan Duration { get; }

    /// <summary>프레임레이트(타임코드용).</summary>
    public double FrameRate { get; }

    public ClipSegment(string filePath, TimeSpan duration, double frameRate)
    {
        FilePath = filePath;
        Duration = duration;
        FrameRate = frameRate;
        _end = duration;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private TimeSpan _start;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private TimeSpan _end;

    /// <summary>목록 표시용 번호(1부터).</summary>
    [ObservableProperty] private int _displayIndex;

    /// <summary>목록에서 현재 선택된 항목 여부.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVisibility))]
    private bool _isSelected;

    public Microsoft.UI.Xaml.Visibility SelectedVisibility =>
        IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>미리보기 회전 각도 (0/90/180/270).</summary>
    public int VideoRotation { get; set; }
    /// <summary>미리보기 좌우 반전.</summary>
    public bool FlipH { get; set; }
    /// <summary>미리보기 상하 반전.</summary>
    public bool FlipV { get; set; }
    /// <summary>미리보기 볼륨 (0.0 ~ 1.0).</summary>
    public double Volume { get; set; } = 1.0;

    public string FileName => Path.GetFileName(FilePath);

    public string RangeText =>
        $"{Start:hh\\:mm\\:ss\\.ff} ~ {End:hh\\:mm\\:ss\\.ff}";

    public string DurationText => Duration.ToString(@"mm\:ss");
}
