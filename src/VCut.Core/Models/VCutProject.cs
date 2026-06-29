using VCut.Core.Models;

namespace VCut.Core.Models;

/// <summary>
/// 작업 상태(파일 목록 + 구간 설정 + 변환 설정)를 담는 프로젝트 모델.
/// docx의 '프로젝트 파일 저장/열기(*.bcpf)'에 대응하며, .vcproj(JSON)로 직렬화.
/// </summary>
public sealed class VCutProject
{
    public int Version { get; set; } = 1;

    /// <summary>편집 대상 클립 목록(각 클립은 여러 구간을 가질 수 있음).</summary>
    public List<ProjectClip> Clips { get; set; } = [];

    /// <summary>고속 모드 여부.</summary>
    public bool FastMode { get; set; } = true;

    /// <summary>여러 구간/파일을 하나로 합칠지 여부.</summary>
    public bool JoinSegments { get; set; }

    /// <summary>마지막으로 사용한 화면 ("trim"/"split"/"merge").</summary>
    public string LastScreen { get; set; } = "trim";

    public ProjectSettings Settings { get; set; } = new();
}

/// <summary>프로젝트 내 클립 하나(파일 경로 + 구간들).</summary>
public sealed class ProjectClip
{
    public string Path { get; set; } = "";
    public List<ProjectRange> Ranges { get; set; } = [];
}

/// <summary>구간(초 단위로 직렬화).</summary>
public sealed class ProjectRange
{
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }

    public MediaRange ToMediaRange() =>
        new(TimeSpan.FromSeconds(StartSeconds), TimeSpan.FromSeconds(EndSeconds));

    public static ProjectRange From(MediaRange r) =>
        new() { StartSeconds = r.Start.TotalSeconds, EndSeconds = r.End.TotalSeconds };
}

/// <summary>변환 설정 스냅샷(직렬화용). <see cref="ConversionSettings"/>와 상호 변환.</summary>
public sealed class ProjectSettings
{
    public ContainerFormat Container { get; set; } = ContainerFormat.Mp4;
    public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;
    public AudioCodec AudioCodec { get; set; } = AudioCodec.Aac;
    public HardwareAccel HardwareAccel { get; set; } = HardwareAccel.None;
    public int Quality { get; set; } = 80;
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public double Speed { get; set; } = 1.0;
    public bool Normalize { get; set; }
    public bool RemoveAudio { get; set; }
    public bool MoovAtFront { get; set; }
    public bool WritePlaybackInfo { get; set; }

    public ConversionSettings ToConversionSettings() => new()
    {
        Container = Container,
        VideoCodec = VideoCodec,
        AudioCodec = AudioCodec,
        HardwareAccel = HardwareAccel,
        VideoQuality = Quality,
        Width = Width,
        Height = Height,
        SizeMode = Width > 0 && Height > 0 ? VideoSizeMode.Fixed : VideoSizeMode.KeepOriginal,
        FrameRate = FrameRate,
        Speed = Speed,
        Normalize = Normalize,
        RemoveAudio = RemoveAudio,
        MoovAtFront = MoovAtFront,
        WritePlaybackInfo = WritePlaybackInfo,
    };

    public static ProjectSettings From(ConversionSettings s) => new()
    {
        Container = s.Container,
        VideoCodec = s.VideoCodec,
        AudioCodec = s.AudioCodec,
        HardwareAccel = s.HardwareAccel,
        Quality = s.VideoQuality,
        Width = s.Width,
        Height = s.Height,
        FrameRate = s.FrameRate,
        Speed = s.Speed,
        Normalize = s.Normalize,
        RemoveAudio = s.RemoveAudio,
        MoovAtFront = s.MoovAtFront,
        WritePlaybackInfo = s.WritePlaybackInfo,
    };
}
