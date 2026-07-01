using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VCut.App.Locale;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace VCut.App.ViewModels;

/// <summary>'자르기 구간 목록'의 항목 하나 — 파일 + 편집 구간(시작/끝).</summary>
public sealed partial class ClipSegment : ObservableObject
{
    public string FilePath { get; }

    /// <summary>원본 전체 길이.</summary>
    public TimeSpan Duration { get; }

    /// <summary>프레임레이트(타임코드용).</summary>
    public double FrameRate { get; }

    public bool IsMissing { get; }

    public ClipSegment(string filePath, TimeSpan duration, double frameRate, bool isMissing = false)
    {
        FilePath = filePath;
        Duration = duration;
        FrameRate = frameRate;
        IsMissing = isMissing;
        _end = duration;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private TimeSpan _start;
    partial void OnStartChanged(TimeSpan value) => RangeChanged?.Invoke(this);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private TimeSpan _end;
    partial void OnEndChanged(TimeSpan value) => RangeChanged?.Invoke(this);

    /// <summary>Start 또는 End가 바뀔 때만 발화. ViewModel이 구독해 TotalDuration·IsModified를 갱신한다.</summary>
    public event Action<ClipSegment>? RangeChanged;

    /// <summary>목록 표시용 번호(1부터).</summary>
    [ObservableProperty] private int _displayIndex;

    /// <summary>목록에서 현재 선택된 항목 여부.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVisibility))]
    private bool _isSelected;

    public Microsoft.UI.Xaml.Visibility SelectedVisibility =>
        IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string MissingText => Loc.Get("clip.missing");

    public Microsoft.UI.Xaml.Visibility MissingVisibility =>
        IsMissing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ExistsVisibility =>
        IsMissing ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public double MissingOpacity => IsMissing ? 0.45 : 1.0;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailFallbackVisibility))]
    private ImageSource? _thumbnailSource;

    /// <summary>썸네일 로딩 전 또는 실패 시 번호를 표시.</summary>
    public Microsoft.UI.Xaml.Visibility ThumbnailFallbackVisibility =>
        ThumbnailSource is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>Windows Shell 썸네일 캐시에서 비동기로 썸네일을 로드한다.</summary>
    public async Task LoadThumbnailAsync()
    {
        if (IsMissing) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(FilePath);
            using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 128);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(thumb);
            ThumbnailSource = bmp;
        }
        catch { }
    }
}
