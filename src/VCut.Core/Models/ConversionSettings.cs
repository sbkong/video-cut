namespace VCut.Core.Models;

/// <summary>
/// 변환 모드 상세 설정. docx의 [출력 설정 &gt; 변환 모드 &gt; 상세 설정] 항목 전체에 대응.
/// 고속 모드에서는 대부분 무시되고 스트림 복사만 수행됨.
/// </summary>
public sealed class ConversionSettings
{
    // ── 파일 형식 ──
    public ContainerFormat Container { get; set; } = ContainerFormat.Mp4;

    // ── 비디오 ──
    public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;
    public HardwareAccel HardwareAccel { get; set; } = HardwareAccel.None;

    /// <summary>프로파일(H.264/HEVC): "baseline" | "main" | "high". null이면 인코더 기본값.</summary>
    public string? Profile { get; set; }

    public RateControl VideoRateControl { get; set; } = RateControl.Vbr;

    /// <summary>품질 값(0~100). VBR에서 사용. 80=원본 수준, 50~60=용량 절감.</summary>
    public int VideoQuality { get; set; } = 80;

    /// <summary>비트레이트(kbps). CBR 또는 VBR 상한에 사용.</summary>
    public int VideoBitrateKbps { get; set; } = 8000;

    // ── 비디오 크기 ──
    public VideoSizeMode SizeMode { get; set; } = VideoSizeMode.KeepOriginal;
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>프레임레이트(FPS). 0이면 원본 유지.</summary>
    public double FrameRate { get; set; }

    public Deinterlace Deinterlace { get; set; } = Deinterlace.Auto;

    // ── 회전/반전 ──
    public Rotation Rotation { get; set; } = Rotation.None;
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }

    // ── 재생 속도(배속) ──
    /// <summary>재생 속도 배율(0.1 ~ 99.9). 1.0이면 원본 속도.</summary>
    public double Speed { get; set; } = 1.0;

    // ── 오디오 ──
    public AudioCodec AudioCodec { get; set; } = AudioCodec.Aac;
    public RateControl AudioRateControl { get; set; } = RateControl.Vbr;

    /// <summary>오디오 비트레이트(kbps). 192=DAB/MP3, 224~320=CD 품질.</summary>
    public int AudioBitrateKbps { get; set; } = 192;

    /// <summary>채널 수. 0이면 원본 유지(1=모노, 2=스테레오).</summary>
    public int AudioChannels { get; set; }

    /// <summary>샘플레이트(Hz). 0이면 원본 유지(44100=CD, 48000=DV).</summary>
    public int AudioSampleRate { get; set; }

    /// <summary>노멀라이즈 — 소리 크기를 고르게 조정(loudnorm).</summary>
    public bool Normalize { get; set; }

    // ── 출력/기타 옵션 ──
    /// <summary>오디오 트랙 추출(.mp3) — 비디오를 버리고 오디오만 저장.</summary>
    public bool ExtractAudioOnly { get; set; }

    /// <summary>오디오 트랙 제거 — 무음 영상으로 저장.</summary>
    public bool RemoveAudio { get; set; }

    /// <summary>MOOV(인덱스)를 파일 앞부분에 저장 — MP4 스트리밍 재생용(faststart).</summary>
    public bool MoovAtFront { get; set; }

    /// <summary>합치기/구간합치기 시 각 구간의 누적 재생시간을 .txt로 저장(유튜브 챕터용).</summary>
    public bool WritePlaybackInfo { get; set; }

    /// <summary>편집에 사용할 비디오 스트림 인덱스. null이면 전체.</summary>
    public int? SelectedVideoStreamIndex { get; set; }

    /// <summary>편집에 사용할 오디오 스트림 인덱스. null이면 전체.</summary>
    public int? SelectedAudioStreamIndex { get; set; }

    /// <summary>회전·반전·배속은 스트림 복사가 불가능하므로 변환 모드가 강제됨.</summary>
    public bool RequiresReencode =>
        Rotation != Rotation.None ||
        FlipHorizontal || FlipVertical ||
        Math.Abs(Speed - 1.0) > 0.001 ||
        SizeMode != VideoSizeMode.KeepOriginal ||
        FrameRate > 0 ||
        Normalize;

    /// <summary>docx 규칙: 4.01배속 이상이면 오디오 트랙이 자동 제거됨.</summary>
    public const double AudioDropSpeedThreshold = 4.01;

    public string ContainerExtension => Container switch
    {
        ContainerFormat.Mp4 => ".mp4",
        ContainerFormat.Mkv => ".mkv",
        ContainerFormat.WebM => ".webm",
        ContainerFormat.Avi => ".avi",
        _ => ".mp4",
    };

    public ConversionSettings Clone() => (ConversionSettings)MemberwiseClone();
}
