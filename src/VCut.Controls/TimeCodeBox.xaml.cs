using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VCut.Controls;

/// <summary>
/// [시 : 분 : 초 : 프레임] 단위로 구간 시점을 정밀 입력하는 위젯. docx '구간 미세 조정'.
/// 마우스 휠로 각 칸을 증감하고, 값 변경 시 <see cref="Time"/>가 갱신됨.
/// </summary>
public sealed partial class TimeCodeBox : UserControl
{
    private bool _updating;

    public TimeCodeBox()
    {
        InitializeComponent();
        HookField(HourBox, 0);
        HookField(MinuteBox, 1);
        HookField(SecondBox, 2);
        HookField(FrameBox, 3);
        UpdateFields(Time);
    }

    /// <summary>현재 시점.</summary>
    public TimeSpan Time
    {
        get => (TimeSpan)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(nameof(Time), typeof(TimeSpan), typeof(TimeCodeBox),
            new PropertyMetadata(TimeSpan.Zero, OnTimeChanged));

    /// <summary>초당 프레임 수(프레임 칸 범위/환산에 사용).</summary>
    public double FrameRate
    {
        get => (double)GetValue(FrameRateProperty);
        set => SetValue(FrameRateProperty, value);
    }

    public static readonly DependencyProperty FrameRateProperty =
        DependencyProperty.Register(nameof(FrameRate), typeof(double), typeof(TimeCodeBox),
            new PropertyMetadata(30.0));

    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (TimeCodeBox)d;
        if (!box._updating) box.UpdateFields((TimeSpan)e.NewValue);
    }

    private void HookField(TextBox box, int unit)
    {
        box.PointerWheelChanged += (s, e) =>
        {
            var pt = e.GetCurrentPoint(box);
            int dir = pt.Properties.MouseWheelDelta > 0 ? 1 : -1;
            Adjust(unit, dir);
            e.Handled = true;
        };
        box.LostFocus += (s, e) => CommitFromFields();
        box.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Up) { Adjust(unit, 1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Down) { Adjust(unit, -1); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Enter) { CommitFromFields(); e.Handled = true; }
        };
    }

    private double Fps => FrameRate > 0 ? FrameRate : 30.0;

    private void Adjust(int unit, int dir)
    {
        var delta = unit switch
        {
            0 => TimeSpan.FromHours(dir),
            1 => TimeSpan.FromMinutes(dir),
            2 => TimeSpan.FromSeconds(dir),
            _ => TimeSpan.FromSeconds(dir / Fps),
        };
        var next = Time + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        Time = next;
    }

    private void CommitFromFields()
    {
        int h = ParseField(HourBox);
        int m = ParseField(MinuteBox);
        int s = ParseField(SecondBox);
        int f = ParseField(FrameBox);
        var t = new TimeSpan(0, h, m, s) + TimeSpan.FromSeconds(f / Fps);
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        _updating = true;
        Time = t;
        _updating = false;
        UpdateFields(t);
    }

    private void UpdateFields(TimeSpan t)
    {
        if (HourBox is null) return;
        int totalFrames = (int)Math.Round((t.Milliseconds / 1000.0) * Fps);
        if (totalFrames >= Fps) totalFrames = (int)Fps - 1;
        HourBox.Text = ((int)t.TotalHours).ToString("D2");
        MinuteBox.Text = t.Minutes.ToString("D2");
        SecondBox.Text = t.Seconds.ToString("D2");
        FrameBox.Text = totalFrames.ToString("D2");
    }

    private static int ParseField(TextBox box) =>
        int.TryParse(box.Text, out var v) && v >= 0 ? v : 0;
}
