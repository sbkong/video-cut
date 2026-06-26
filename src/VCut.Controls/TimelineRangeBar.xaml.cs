using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VCut.Controls;

/// <summary>
/// 동영상 타임라인. 전체 길이 위에 파란색 시작/끝 핸들과 주황 재생헤드를 표시하고
/// 드래그로 구간/위치를 조정. docx 메인창의 '구간 시작/끝, 재생 상태바'에 대응.
/// </summary>
public sealed partial class TimelineRangeBar : UserControl
{
    private Border? _dragging;

    public TimelineRangeBar() => InitializeComponent();

    /// <summary>사용자가 재생헤드를 옮겼을 때(탐색 요청).</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    // ── 종속성 속성 ──

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(TimelineRangeBar),
            new PropertyMetadata(TimeSpan.Zero, OnLayoutPropChanged));

    public TimeSpan Start
    {
        get => (TimeSpan)GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }
    public static readonly DependencyProperty StartProperty =
        DependencyProperty.Register(nameof(Start), typeof(TimeSpan), typeof(TimelineRangeBar),
            new PropertyMetadata(TimeSpan.Zero, OnLayoutPropChanged));

    public TimeSpan End
    {
        get => (TimeSpan)GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }
    public static readonly DependencyProperty EndProperty =
        DependencyProperty.Register(nameof(End), typeof(TimeSpan), typeof(TimelineRangeBar),
            new PropertyMetadata(TimeSpan.Zero, OnLayoutPropChanged));

    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }
    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(TimelineRangeBar),
            new PropertyMetadata(TimeSpan.Zero, OnLayoutPropChanged));

    private static void OnLayoutPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TimelineRangeBar)d).Relayout();

    // ── 레이아웃 ──

    private double TrackWidth => Track.ActualWidth;
    private double TotalSeconds => Duration.TotalSeconds > 0 ? Duration.TotalSeconds : 1;

    private double TimeToX(TimeSpan t) =>
        Math.Clamp(t.TotalSeconds / TotalSeconds, 0, 1) * TrackWidth;

    private TimeSpan XToTime(double x) =>
        TimeSpan.FromSeconds(Math.Clamp(x / Math.Max(TrackWidth, 1), 0, 1) * TotalSeconds);

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => Relayout();

    private void Relayout()
    {
        if (Track is null || TrackWidth <= 0) return;

        double midY = Track.ActualHeight / 2;
        Rail.Width = TrackWidth;
        Canvas.SetTop(Rail, midY - Rail.Height / 2);
        Canvas.SetLeft(Rail, 0);

        double sx = TimeToX(Start);
        double ex = TimeToX(End <= Start ? Duration : End);
        Selection.Width = Math.Max(0, ex - sx);
        Canvas.SetLeft(Selection, sx);
        Canvas.SetTop(Selection, midY - Selection.Height / 2);

        Canvas.SetLeft(StartHandle, sx - StartHandle.Width / 2);
        Canvas.SetTop(StartHandle, midY - StartHandle.Height / 2);
        Canvas.SetLeft(EndHandle, ex - EndHandle.Width / 2);
        Canvas.SetTop(EndHandle, midY - EndHandle.Height / 2);

        double px = TimeToX(Position);
        Canvas.SetLeft(Playhead, px - Playhead.Width / 2);
        Canvas.SetTop(Playhead, midY - Playhead.Height / 2);
    }

    // ── 핸들 드래그 ──

    private void OnHandlePressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = sender as Border;
        _dragging?.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnHandleMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || !ReferenceEquals(_dragging, sender)) return;
        double x = e.GetCurrentPoint(Track).Position.X;
        var t = XToTime(x);
        if (_dragging == StartHandle)
        {
            if (t >= End) t = End - TimeSpan.FromMilliseconds(100);
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            Start = t;
        }
        else if (_dragging == EndHandle)
        {
            if (t <= Start) t = Start + TimeSpan.FromMilliseconds(100);
            if (t > Duration) t = Duration;
            End = t;
        }
        e.Handled = true;
    }

    private void OnHandleReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is not null)
        {
            _dragging.ReleasePointerCapture(e.Pointer);
            _dragging = null;
            e.Handled = true;
        }
    }

    // ── 트랙 클릭 → 탐색 ──

    private void OnTrackPressed(object sender, PointerRoutedEventArgs e)
    {
        // 핸들을 직접 누른 경우는 위에서 처리됨.
        double x = e.GetCurrentPoint(Track).Position.X;
        var t = XToTime(x);
        Position = t;
        SeekRequested?.Invoke(this, t);
    }
}
