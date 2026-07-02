using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
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
    private uint _naturalVideoWidth;
    private uint _naturalVideoHeight;

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

        // 컨테이너 크기가 바뀌면 클립 갱신 + 회전 스케일 재계산
        Player.SizeChanged += (_, _) => { UpdatePlayerClip(); ApplyTransform(); };
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

    // PlayerBorder를 클립해 스케일 업 시 블랙바가 하단 컨트롤을 가리지 않게 방지
    private void UpdatePlayerClip()
    {
        if (Player.ActualWidth > 0 && Player.ActualHeight > 0)
            PlayerBorder.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, Player.ActualWidth, Player.ActualHeight),
            };
    }

    private void ApplyTransform()
    {
        PlayerTransform.Rotation = VideoRotation;

        double scale = 1.0;
        bool rotated90 = VideoRotation % 180 != 0;

        if (rotated90 && Player.ActualWidth > 0 && Player.ActualHeight > 0)
        {
            double wc = Player.ActualWidth, hc = Player.ActualHeight;

            if (_naturalVideoWidth > 0 && _naturalVideoHeight > 0)
            {
                // 영상 자연 해상도 기준으로 계산:
                //   origScale = 원본 영상이 컨테이너에 맞는 비율
                //   fitScale  = 90° 회전 후 영상이 컨테이너에 맞는 비율
                //   => MediaPlayerElement 전체를 fitScale/origScale 배 스케일
                double wv = _naturalVideoWidth, hv = _naturalVideoHeight;
                double origScale = Math.Min(wc / wv, hc / hv);
                double fitScale  = Math.Min(wc / hv, hc / wv);
                if (origScale > 0) scale = fitScale / origScale;
            }
            else
            {
                // 자연 해상도 미확정 시 단순 비율 축소(잘림 없는 안전 폴백)
                scale = Math.Min(wc, hc) / Math.Max(wc, hc);
            }
        }

        PlayerTransform.ScaleX = (FlipH ? -1 : 1) * scale;
        PlayerTransform.ScaleY = (FlipV ? -1 : 1) * scale;
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
        PositionText.Text = "00:00:00.00 / " + Format(Duration);
    }

    private void OnTrimStartChanged()
    {
        if (_updatingRange) return;
        _updatingRange = true;
        RangeBar.Start = TrimStart;
        _updatingRange = false;
        UpdateDurationText();
    }

    private void OnTrimEndChanged()
    {
        if (_updatingRange) return;
        _updatingRange = true;
        RangeBar.End = TrimEnd;
        _updatingRange = false;
        UpdateDurationText();
    }

    private void UpdateDurationText()
    {
        var d = TrimEnd > TrimStart ? TrimEnd - TrimStart : TimeSpan.Zero;
        DurationText.Text = Format(d);
    }

    // ── 미디어 소스 ──────────────────────────────────────────────────────

    public void SetSource(string filePath, TimeSpan duration)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        Player.Source = MediaSource.CreateFromUri(new Uri(filePath));
        PositionText.Text = "00:00:00.00 / " + Format(duration);
        RangeBar.Position  = TimeSpan.Zero;
    }

    public void Clear()
    {
        _naturalVideoWidth  = 0;
        _naturalVideoHeight = 0;
        Player.MediaPlayer.Pause();
        Player.Source = null;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RangeBar.Position = TimeSpan.Zero;
        RangeBar.Duration = TimeSpan.Zero;
        PositionText.Text  = "00:00:00.00 / 00:00:00.00";
        DurationText.Text  = "00:00:00.00";
    }

    public bool IsPlaying => Session.PlaybackState == MediaPlaybackState.Playing;

    private MediaPlaybackSession Session => Player.MediaPlayer.PlaybackSession;

    // ── 미디어 이벤트 ────────────────────────────────────────────────────

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _naturalVideoWidth  = sender.PlaybackSession.NaturalVideoWidth;
            _naturalVideoHeight = sender.PlaybackSession.NaturalVideoHeight;
            ApplyTransform(); // 자연 해상도 확정 후 스케일 재계산
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
            PositionText.Text = "00:00:00.00 / " + Format(dur);
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
            PositionText.Text = Format(pos) + " / " + Format(Duration);
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
        if (LoopButton.IsChecked == true && (t < TrimStart || t >= TrimEnd))
        {
            LoopButton.IsChecked = false;
            OnLoopChanged(this, new RoutedEventArgs());
        }
        Session.Position = t;
    }

    private void OnRangeBarStartChanged(object? sender, TimeSpan t)
    {
        _updatingRange = true;
        TrimStart = t;
        _updatingRange = false;
        UpdateDurationText();
    }

    private void OnRangeBarEndChanged(object? sender, TimeSpan t)
    {
        _updatingRange = true;
        TrimEnd = t;
        _updatingRange = false;
        UpdateDurationText();
    }

    // ── 트랜스포트 버튼 ───────────────────────────────────────────────────

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlayPause();

    public void TogglePlayPause()
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

    public void StepFrames(int frames) => Seek(frames / _frameRate);

    public void Seek(double seconds)
    {
        var next = Session.Position + TimeSpan.FromSeconds(seconds);
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
