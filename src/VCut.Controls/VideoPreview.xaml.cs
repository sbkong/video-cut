using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace VCut.Controls;

/// <summary>
/// 재사용 가능한 동영상 미리보기 컨트롤. MediaPlayerElement 위에 프레임 단위 이동과
/// 시점 표시를 얹음. VCut.App 및 외부 WinUI 3 앱에서 그대로 임베딩 가능.
/// </summary>
public sealed partial class VideoPreview : UserControl
{
    private readonly DispatcherQueue _dispatcher;
    private double _frameRate = 30.0;

    /// <summary>재생 위치가 바뀔 때(타임라인 재생헤드 동기화용).</summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    public VideoPreview()
    {
        InitializeComponent();
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Player.MediaPlayer.PlaybackSession.PositionChanged += OnPositionChanged;
    }

    /// <summary>현재 미디어의 초당 프레임 수. 프레임 단위 이동 간격 계산에 사용.</summary>
    public double FrameRate
    {
        get => _frameRate;
        set => _frameRate = value > 0 ? value : 30.0;
    }

    /// <summary>미리보기에 동영상을 로드.</summary>
    public void SetSource(string filePath)
    {
        Player.Source = MediaSource.CreateFromUri(new Uri(filePath));
    }

    /// <summary>미리보기를 비움.</summary>
    public void Clear()
    {
        Player.MediaPlayer.Pause();
        Player.Source = null;
        PositionText.Text = "00:00:00.000 / 00:00:00.000";
    }

    /// <summary>현재 재생 위치.</summary>
    public TimeSpan Position
    {
        get => Player.MediaPlayer.PlaybackSession.Position;
        set => Player.MediaPlayer.PlaybackSession.Position = value;
    }

    private MediaPlaybackSession Session => Player.MediaPlayer.PlaybackSession;

    private void StepFrames(int frames)
    {
        var delta = TimeSpan.FromSeconds(frames / _frameRate);
        var next = Session.Position + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        if (Session.NaturalDuration > TimeSpan.Zero && next > Session.NaturalDuration)
            next = Session.NaturalDuration;
        Session.Position = next;
    }

    private void OnPrevFrame(object sender, RoutedEventArgs e) => StepFrames(-1);
    private void OnNextFrame(object sender, RoutedEventArgs e) => StepFrames(1);

    // 키프레임 정밀 탐색은 디코더 접근이 필요하므로, 근사로 1초(≈GOP) 단위 이동.
    private void OnPrevKeyframe(object sender, RoutedEventArgs e) => StepFrames((int)-_frameRate);
    private void OnNextKeyframe(object sender, RoutedEventArgs e) => StepFrames((int)_frameRate);

    private void OnPositionChanged(MediaPlaybackSession sender, object args)
    {
        var pos = sender.Position;
        var dur = sender.NaturalDuration;
        _dispatcher.TryEnqueue(() =>
        {
            PositionText.Text = $"{Format(pos)} / {Format(dur)}";
            PositionChanged?.Invoke(this, pos);
        });
    }

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss\.fff");
}
