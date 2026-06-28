using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VCut.Controls;

public sealed partial class TimelineRangeBar : UserControl
{
    private Border? _dragging;
    private double  _dragX;
    private bool    _draggingPlayhead;
    private bool    _scrubbingTrack;
    private double  _lsx, _lex;

    public TimelineRangeBar() => InitializeComponent();

    // ── 이벤트 ────────────────────────────────────────────────────────────

    public event EventHandler<TimeSpan>? SeekRequested;
    public event EventHandler<TimeSpan>? StartChanged;
    public event EventHandler<TimeSpan>? EndChanged;

    // ── 종속성 속성 ───────────────────────────────────────────────────────

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

    public bool IsLoopActive
    {
        get => (bool)GetValue(IsLoopActiveProperty);
        set => SetValue(IsLoopActiveProperty, value);
    }
    public static readonly DependencyProperty IsLoopActiveProperty =
        DependencyProperty.Register(nameof(IsLoopActive), typeof(bool), typeof(TimelineRangeBar),
            new PropertyMetadata(false, (d, e) => ((TimelineRangeBar)d).UpdateSelectionColor()));

    private void UpdateSelectionColor()
    {
        if (Selection is null) return;
        Selection.Background = IsLoopActive
            ? new SolidColorBrush(Color.FromArgb(204, 210, 50, 50))
            : new SolidColorBrush(Color.FromArgb(204, 53, 116, 240));
    }

    private static void OnLayoutPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (TimelineRangeBar)d;
        if (self._dragging is not null && e.Property == PositionProperty) { self.Relayout(); return; }
        self.Relayout();
    }

    // ── 레이아웃 ──────────────────────────────────────────────────────────

    private double TrackWidth   => Track.ActualWidth;
    private double TotalSeconds => Duration.TotalSeconds > 0 ? Duration.TotalSeconds : 1;

    private double   TimeToX(TimeSpan t) =>
        Math.Clamp(t.TotalSeconds / TotalSeconds, 0, 1) * TrackWidth;

    private TimeSpan XToTime(double x) =>
        TimeSpan.FromSeconds(Math.Clamp(x / Math.Max(TrackWidth, 1), 0, 1) * TotalSeconds);

    private void OnTrackSizeChanged(object sender, SizeChangedEventArgs e) => Relayout();

    private void Relayout()
    {
        if (Track is null || TrackWidth <= 0) return;

        double midY = Track.ActualHeight / 2;

        Rail.Width = TrackWidth;
        Canvas.SetLeft(Rail, 0);
        Canvas.SetTop(Rail, midY - Rail.Height / 2);

        double px = TimeToX(Position);
        Progress.Width = Math.Max(0, px);
        Canvas.SetLeft(Progress, 0);
        Canvas.SetTop(Progress, midY - Progress.Height / 2);
        Canvas.SetLeft(Playhead, px - Playhead.Width / 2);
        Canvas.SetTop(Playhead, midY - Playhead.Height / 2);

        if (_dragging is not null) return;

        double sx = TimeToX(Start);
        double ex = TimeToX(End <= Start ? Duration : End);
        _lsx = sx;
        _lex = ex;

        Selection.Width = Math.Max(0, ex - sx);
        Canvas.SetLeft(Selection, sx);
        Canvas.SetTop(Selection, midY - Selection.Height / 2);
        Canvas.SetLeft(StartHandle, sx - StartHandle.Width / 2);
        Canvas.SetTop(StartHandle, midY - StartHandle.Height / 2);
        Canvas.SetLeft(EndHandle,  ex - EndHandle.Width / 2);
        Canvas.SetTop(EndHandle,   midY - EndHandle.Height / 2);
    }

    // ── 핸들 드래그 ──────────────────────────────────────────────────────

    private void OnHandlePressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = sender as Border;
        _dragging?.CapturePointer(e.Pointer);
        _lsx   = Canvas.GetLeft(StartHandle) + StartHandle.Width / 2;
        _lex   = Canvas.GetLeft(EndHandle)   + EndHandle.Width   / 2;
        _dragX = _dragging == StartHandle ? _lsx : _lex;
        e.Handled = true;
    }

    private void OnHandleMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null || !ReferenceEquals(_dragging, sender)) return;

        double x = Math.Clamp(e.GetCurrentPoint(Track).Position.X, 0, TrackWidth);

        if (_dragging == EndHandle)
        {
            x = Math.Clamp(x, _lsx + 1, TrackWidth);
            Canvas.SetLeft(EndHandle, x - EndHandle.Width / 2);
            Selection.Width = Math.Max(0, x - _lsx);
            EndChanged?.Invoke(this, XToTime(x));
        }
        else
        {
            x = Math.Clamp(x, 0, _lex - 1);
            Canvas.SetLeft(StartHandle, x - StartHandle.Width / 2);
            Canvas.SetLeft(Selection, x);
            Selection.Width = Math.Max(0, _lex - x);
            StartChanged?.Invoke(this, XToTime(x));
        }

        _dragX = x;
        e.Handled = true;
    }

    private void OnHandleReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is not null)
        {
            var t        = XToTime(_dragX);
            var wasStart = _dragging == StartHandle;
            var captured = _dragging;
            captured.ReleasePointerCapture(e.Pointer);
            _dragging = null;
            if (wasStart) { Start = t; StartChanged?.Invoke(this, t); }
            else          { End   = t; EndChanged?.Invoke(this, t); }
            e.Handled = true;
        }
    }

    // ── 재생헤드 드래그 ───────────────────────────────────────────────────

    private void OnPlayheadPressed(object sender, PointerRoutedEventArgs e)
    {
        _draggingPlayhead = true;
        Playhead.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPlayheadMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingPlayhead) return;
        var t = XToTime(e.GetCurrentPoint(Track).Position.X);
        Position = t;
        SeekRequested?.Invoke(this, t);
        e.Handled = true;
    }

    private void OnPlayheadReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingPlayhead) return;
        Playhead.ReleasePointerCapture(e.Pointer);
        _draggingPlayhead = false;
        e.Handled = true;
    }

    // ── 트랙 클릭 + 스크럽 ───────────────────────────────────────────────

    private void OnTrackPressed(object sender, PointerRoutedEventArgs e)
    {
        _scrubbingTrack = true;
        Track.CapturePointer(e.Pointer);
        var pt = e.GetCurrentPoint(Track).Position;
        UpdateHoverTip(pt.X);
        var t = XToTime(pt.X);
        Position = t;
        SeekRequested?.Invoke(this, t);
        e.Handled = true;
    }

    private void OnTrackMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(Track).Position;
        UpdateHoverTip(pt.X);
        if (!_scrubbingTrack) return;
        var t = XToTime(pt.X);
        Position = t;
        SeekRequested?.Invoke(this, t);
        e.Handled = true;
    }

    private void OnTrackReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_scrubbingTrack) return;
        Track.ReleasePointerCapture(e.Pointer);
        _scrubbingTrack = false;
        e.Handled = true;
    }

    // ── 호버 툴팁 ────────────────────────────────────────────────────────

    private void OnTrackEntered(object sender, PointerRoutedEventArgs e)
    {
        HoverTip.Visibility = Visibility.Visible;
    }

    private void OnTrackExited(object sender, PointerRoutedEventArgs e)
    {
        HoverTip.Visibility = Visibility.Collapsed;
    }

    private void UpdateHoverTip(double x)
    {
        x = Math.Clamp(x, 0, TrackWidth);
        HoverTipText.Text = Format(XToTime(x));
        double tipW = HoverTip.ActualWidth > 0 ? HoverTip.ActualWidth : 62;
        Canvas.SetLeft(HoverTip, Math.Clamp(x - tipW / 2, 0, Math.Max(0, TrackWidth - tipW)));
        Canvas.SetTop(HoverTip, 0);
        Canvas.SetZIndex(HoverTip, 100);
    }

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.ff");
}
