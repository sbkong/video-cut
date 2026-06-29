using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI;

namespace VCut.Controls;

public sealed partial class VideoPreview : UserControl
{
    private readonly DispatcherQueue _dispatcher;
    private double _frameRate = 30.0;
    private bool _isMuted;
    private double _prevVolume = 1.0;
    private bool _updatingRange;
    private bool _settingVolume;

    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<double>?   VolumeChanged;

    public VideoPreview()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        var session = Player.MediaPlayer.PlaybackSession;
        session.PositionChanged      += OnPositionChanged;
        session.PlaybackStateChanged += OnPlaybackStateChanged;
        Player.MediaPlayer.MediaOpened += OnMediaOpened;

        RangeBar.SeekRequested += OnRangeBarSeek;
        RangeBar.StartChanged  += OnRangeBarStartChanged;
        RangeBar.EndChanged    += OnRangeBarEndChanged;
    }

    // ── 공개 속성 ────────────────────────────────────────────────────────

    public double FrameRate
    {
        get => _frameRate;
        set => _frameRate = value > 0 ? value : 30.0;
    }

    public bool IsLooping => LoopButton.IsChecked == true;

    public double Volume
    {
        get => Player.MediaPlayer.Volume;
        set
        {
            _settingVolume = true;
            VolumeSlider.Value = Math.Clamp(value, 0, 1) * 100;
            _settingVolume = false;
        }
    }

    public TimeSpan Position
    {
        get => Player.MediaPlayer.PlaybackSession.Position;
        set => Player.MediaPlayer.PlaybackSession.Position = value;
    }

    // ── 종속성 속성 ──────────────────────────────────────────────────────

    public int VideoRotation
    {
        get => (int)GetValue(VideoRotationProperty);
        set => SetValue(VideoRotationProperty, value);
    }
    public static readonly DependencyProperty VideoRotationProperty =
        DependencyProperty.Register(nameof(VideoRotation), typeof(int), typeof(VideoPreview),
            new PropertyMetadata(0, (d, e) => ((VideoPreview)d).ApplyTransform()));

    public bool FlipH
    {
        get => (bool)GetValue(FlipHProperty);
        set => SetValue(FlipHProperty, value);
    }
    public static readonly DependencyProperty FlipHProperty =
        DependencyProperty.Register(nameof(FlipH), typeof(bool), typeof(VideoPreview),
            new PropertyMetadata(false, (d, e) => ((VideoPreview)d).ApplyTransform()));

    public bool FlipV
    {
        get => (bool)GetValue(FlipVProperty);
        set => SetValue(FlipVProperty, value);
    }
    public static readonly DependencyProperty FlipVProperty =
        DependencyProperty.Register(nameof(FlipV), typeof(bool), typeof(VideoPreview),
            new PropertyMetadata(false, (d, e) => ((VideoPreview)d).ApplyTransform()));

    private void ApplyTransform()
    {
        PlayerTransform.Rotation = VideoRotation;
        PlayerTransform.ScaleX   = FlipH ? -1 : 1;
        PlayerTransform.ScaleY   = FlipV ? -1 : 1;
    }

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }
    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(VideoPreview),
            new PropertyMetadata(TimeSpan.Zero, (d, e) => ((VideoPreview)d).OnDurationChanged()));

    public TimeSpan TrimStart
    {
        get => (TimeSpan)GetValue(TrimStartProperty);
        set => SetValue(TrimStartProperty, value);
    }
    public static readonly DependencyProperty TrimStartProperty =
        DependencyProperty.Register(nameof(TrimStart), typeof(TimeSpan), typeof(VideoPreview),
            new PropertyMetadata(TimeSpan.Zero, (d, e) => ((VideoPreview)d).OnTrimStartChanged()));

    public TimeSpan TrimEnd
    {
        get => (TimeSpan)GetValue(TrimEndProperty);
        set => SetValue(TrimEndProperty, value);
    }
    public static readonly DependencyProperty TrimEndProperty =
        DependencyProperty.Register(nameof(TrimEnd), typeof(TimeSpan), typeof(VideoPreview),
            new PropertyMetadata(TimeSpan.Zero, (d, e) => ((VideoPreview)d).OnTrimEndChanged()));

    private void OnDurationChanged()
    {
        if (_updatingRange) return;
        _updatingRange = true;
        RangeBar.Duration = Duration;
        _updatingRange = false;
        var str = "00:00:00.00 / " + Format(Duration);
        PositionText.Text  = str;
        PositionText2.Text = str;
    }

    private void OnTrimStartChanged()
    {
        if (_updatingRange) return;
        _updatingRange = true;
        RangeBar.Start = TrimStart;
        _updatingRange = false;
    }

    private void OnTrimEndChanged()
    {
        if (_updatingRange) return;
        _updatingRange = true;
        RangeBar.End = TrimEnd;
        _updatingRange = false;
    }

    // ── 미디어 소스 ──────────────────────────────────────────────────────

    public void SetSource(string filePath, TimeSpan duration)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        Player.Source = MediaSource.CreateFromUri(new Uri(filePath));
        var str = "00:00:00.00 / " + Format(duration);
        PositionText.Text  = str;
        PositionText2.Text = str;
        RangeBar.Position  = TimeSpan.Zero;
    }

    public void Clear()
    {
        Player.MediaPlayer.Pause();
        Player.Source = null;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RangeBar.Position = TimeSpan.Zero;
        RangeBar.Duration = TimeSpan.Zero;
        const string zero = "00:00:00.00 / 00:00:00.00";
        PositionText.Text  = zero;
        PositionText2.Text = zero;
    }

    private MediaPlaybackSession Session => Player.MediaPlayer.PlaybackSession;

    // ── 미디어 이벤트 ────────────────────────────────────────────────────

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            var dur = sender.PlaybackSession.NaturalDuration;
            Duration          = dur;
            RangeBar.Duration = dur;
            // TrimStart/TrimEnd는 OnSelectedSegmentChanged에서 이미 세그먼트 값으로 설정됨.
            // 여기서 리셋하면 TwoWay 바인딩을 통해 세그먼트 값이 덮어씌워지므로 건드리지 않음.
            _updatingRange = true;
            RangeBar.Start = TrimStart;
            RangeBar.End   = TrimEnd > TrimStart ? TrimEnd : dur;
            _updatingRange = false;
            var str = "00:00:00.00 / " + Format(dur);
            PositionText.Text  = str;
            PositionText2.Text = str;
        });
    }

    private void OnPositionChanged(MediaPlaybackSession sender, object args)
    {
        var pos = sender.Position;
        _dispatcher.TryEnqueue(() =>
        {
            // 구간 반복 재생
            if (LoopButton.IsChecked == true && TrimEnd > TrimStart && pos >= TrimEnd)
            {
                Session.Position = TrimStart;
                return;
            }

            RangeBar.Position  = pos;
            // sender.NaturalDuration은 영상 전환 직후 0 또는 이전 값일 수 있으므로
            // 바인딩으로 동기화된 Duration DP를 사용
            var str = Format(pos) + " / " + Format(Duration);
            PositionText.Text  = str;
            PositionText2.Text = str;
            PositionChanged?.Invoke(this, pos);
        });
    }

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            // E769=Pause, E768=Play (Segoe MDL2 Assets)
            PlayIcon.Glyph = sender.PlaybackState == MediaPlaybackState.Playing
                ? ((char)0xE769).ToString()
                : ((char)0xE768).ToString();
        });
    }

    // ── RangeBar 이벤트 ──────────────────────────────────────────────────

    private void OnRangeBarSeek(object? sender, TimeSpan t)
    {
        Session.Position = t;
    }

    private void OnRangeBarStartChanged(object? sender, TimeSpan t)
    {
        _updatingRange = true;
        TrimStart = t;
        _updatingRange = false;
    }

    private void OnRangeBarEndChanged(object? sender, TimeSpan t)
    {
        _updatingRange = true;
        TrimEnd = t;
        _updatingRange = false;
    }

    // ── 트랜스포트 버튼 ───────────────────────────────────────────────────

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (Session.PlaybackState == MediaPlaybackState.Playing)
        {
            Player.MediaPlayer.Pause();
        }
        else
        {
            // 구간 반복 모드: 범위 밖이면 TrimStart 부터 재생
            if (LoopButton.IsChecked == true && TrimEnd > TrimStart)
            {
                var pos = Session.Position;
                if (pos < TrimStart || pos >= TrimEnd)
                    Session.Position = TrimStart;
            }
            Player.MediaPlayer.Play();
        }
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        Player.MediaPlayer.Pause();
        Session.Position = TimeSpan.Zero;
    }

    public void ToggleRangePlay()
    {
        if (Session.PlaybackState == MediaPlaybackState.Playing)
        {
            Player.MediaPlayer.Pause();
            return;
        }
        if (LoopButton.IsChecked != true)
        {
            LoopButton.IsChecked = true;
            OnLoopChanged(this, new RoutedEventArgs());
        }
        if (TrimEnd > TrimStart && (Session.Position < TrimStart || Session.Position >= TrimEnd))
            Session.Position = TrimStart;
        Player.MediaPlayer.Play();
    }

    public void StopRange()
    {
        Player.MediaPlayer.Pause();
        Session.Position = TrimStart;
    }

    private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var vol = e.NewValue / 100.0;
        Player.MediaPlayer.Volume = vol;
        var nowMuted = vol <= 0;
        if (nowMuted != _isMuted)
        {
            _isMuted = nowMuted;
            Player.MediaPlayer.IsMuted = _isMuted;
        }
        UpdateVolumeIcon();
        if (!_settingVolume) VolumeChanged?.Invoke(this, vol);
    }

    private void OnMuteToggle(object sender, RoutedEventArgs e)
    {
        if (_isMuted)
        {
            _isMuted = false;
            Player.MediaPlayer.IsMuted = false;
            var restore = _prevVolume > 0 ? _prevVolume : 1.0;
            Player.MediaPlayer.Volume = restore;
            VolumeSlider.Value = restore * 100;
        }
        else
        {
            _prevVolume = Player.MediaPlayer.Volume;
            _isMuted = true;
            Player.MediaPlayer.IsMuted = true;
            VolumeSlider.Value = 0;
        }
        UpdateVolumeIcon();
    }

    private void UpdateVolumeIcon()
    {
        VolumeIcon.Glyph = _isMuted || Player.MediaPlayer.Volume <= 0
            ? ((char)0xE74F).ToString()   // 뮤트
            : ((char)0xE767).ToString();  // 볼륨
    }

    private void OnLoopChanged(object sender, RoutedEventArgs e)
    {
        bool active = LoopButton.IsChecked == true;
        RangeBar.IsLoopActive = active;

        // 버튼 테두리를 빨간색/기본으로 전환
        LoopButton.BorderBrush = active
            ? new SolidColorBrush(Color.FromArgb(255, 210, 50, 50))
            : (SolidColorBrush)Application.Current.Resources["BcDividerBrush"];
    }

    // ── 프레임 이동 ───────────────────────────────────────────────────────

    private void StepFrames(int frames)
    {
        var delta = TimeSpan.FromSeconds(frames / _frameRate);
        var next  = Session.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (Session.NaturalDuration > TimeSpan.Zero && next > Session.NaturalDuration)
            next = Session.NaturalDuration;
        Session.Position = next;
    }

    private void OnPrevFrame(object sender, RoutedEventArgs e)    => StepFrames(-1);
    private void OnNextFrame(object sender, RoutedEventArgs e)    => StepFrames(1);
    private void OnPrevKeyframe(object sender, RoutedEventArgs e) => StepFrames((int)-_frameRate);
    private void OnNextKeyframe(object sender, RoutedEventArgs e) => StepFrames((int)_frameRate);

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.ff");
}
