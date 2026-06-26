namespace VCut.Core.Models;

/// <summary>동영상 파일의 전체 미디어 정보(ffprobe 분석 결과).</summary>
public sealed class MediaInfo
{
    public required string FilePath { get; init; }

    /// <summary>전체 재생 시간.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>컨테이너 포맷 이름(예: "mov,mp4,m4a,3gp,3g2,mj2").</summary>
    public string FormatName { get; init; } = "";

    /// <summary>파일 크기(바이트).</summary>
    public long SizeBytes { get; init; }

    /// <summary>전체 비트레이트(bps).</summary>
    public long BitRate { get; init; }

    public IReadOnlyList<VideoStreamInfo> VideoStreams { get; init; } = [];
    public IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; } = [];

    /// <summary>대표(첫 번째) 비디오 스트림. 없으면 null.</summary>
    public VideoStreamInfo? PrimaryVideo => VideoStreams.Count > 0 ? VideoStreams[0] : null;

    /// <summary>대표(첫 번째) 오디오 스트림. 없으면 null.</summary>
    public AudioStreamInfo? PrimaryAudio => AudioStreams.Count > 0 ? AudioStreams[0] : null;

    /// <summary>비디오/오디오 트랙이 각각 2개 이상이면 트랙 선택 UI가 필요함.</summary>
    public bool HasMultipleTracks => VideoStreams.Count > 1 || AudioStreams.Count > 1;
}

/// <summary>비디오 스트림 정보.</summary>
public sealed class VideoStreamInfo
{
    public int Index { get; init; }
    public string CodecName { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>초당 프레임 수(평균).</summary>
    public double FrameRate { get; init; }

    public long BitRate { get; init; }
    public string PixelFormat { get; init; } = "";

    /// <summary>인터레이스 영상 여부(가로줄). 알 수 없으면 null.</summary>
    public bool? IsInterlaced { get; init; }

    /// <summary>컨테이너에 기록된 회전 메타데이터(도).</summary>
    public int RotationDegrees { get; init; }

    public string Resolution => $"{Width}x{Height}";
}

/// <summary>오디오 스트림 정보.</summary>
public sealed class AudioStreamInfo
{
    public int Index { get; init; }
    public string CodecName { get; init; } = "";
    public int Channels { get; init; }
    public int SampleRate { get; init; }
    public long BitRate { get; init; }

    /// <summary>언어 태그(예: "kor", "eng"). 없으면 빈 문자열.</summary>
    public string Language { get; init; } = "";

    public string ChannelLabel => Channels switch
    {
        1 => "모노",
        2 => "스테레오",
        6 => "5.1",
        8 => "7.1",
        _ => $"{Channels}ch",
    };
}
