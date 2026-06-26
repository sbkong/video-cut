namespace VCut.Core.Models;

/// <summary>출력 방식. 고속 모드는 재인코딩 없이 스트림 복사, 변환 모드는 재인코딩.</summary>
public enum OutputMode
{
    /// <summary>고속 모드 — 화질 저하 없이 스트림 복사(-c copy). 필터/배속/회전 불가.</summary>
    Fast,

    /// <summary>변환 모드 — 사용자 설정으로 재인코딩.</summary>
    Convert,
}

/// <summary>출력 컨테이너(파일 형식).</summary>
public enum ContainerFormat
{
    Mp4,
    Mkv,
    WebM,
    Avi,
}

/// <summary>비디오 코덱.</summary>
public enum VideoCodec
{
    /// <summary>스트림 복사(재인코딩 없음).</summary>
    Copy,
    H264,
    Hevc,
    Av1,
    Vp8,
    Vp9,
    Xvid,
    Mpeg1,
    Mpeg4,
    MotionJpeg,
    /// <summary>무압축 YV12(planar 4:2:0). 화질 좋지만 용량 매우 큼.</summary>
    Yv12,
    /// <summary>무압축 RGB24. 화질 좋지만 용량이 YV12보다도 큼.</summary>
    Rgb24,
    /// <summary>비디오 트랙 제거.</summary>
    None,
}

/// <summary>오디오 코덱.</summary>
public enum AudioCodec
{
    /// <summary>스트림 복사(재인코딩 없음).</summary>
    Copy,
    Aac,
    Mp3,
    Mp2,
    Pcm,
    Opus,
    Vorbis,
    Flac,
    /// <summary>오디오 트랙 제거(무음).</summary>
    None,
}

/// <summary>하드웨어 가속 인코더 종류.</summary>
public enum HardwareAccel
{
    /// <summary>소프트웨어 인코딩.</summary>
    None,
    /// <summary>NVIDIA NVENC.</summary>
    Nvenc,
    /// <summary>Intel QuickSync.</summary>
    Qsv,
    /// <summary>AMD AMF.</summary>
    Amf,
}

/// <summary>비트레이트 제어 방식.</summary>
public enum RateControl
{
    /// <summary>가변 비트레이트(품질 기준).</summary>
    Vbr,
    /// <summary>고정 비트레이트.</summary>
    Cbr,
}

/// <summary>출력 해상도 결정 방식.</summary>
public enum VideoSizeMode
{
    /// <summary>원본 유지.</summary>
    KeepOriginal,
    /// <summary>가로 폭 맞춤(세로는 비율 유지).</summary>
    FitWidth,
    /// <summary>세로 폭 맞춤(가로는 비율 유지).</summary>
    FitHeight,
    /// <summary>가로×세로 직접 지정.</summary>
    Fixed,
}

/// <summary>회전 각도.</summary>
public enum Rotation
{
    None = 0,
    R90 = 90,
    R180 = 180,
    R270 = 270,
}

/// <summary>디인터레이스 적용 방식.</summary>
public enum Deinterlace
{
    /// <summary>인터레이스 영상만 자동 처리.</summary>
    Auto,
    /// <summary>항상 적용.</summary>
    Always,
    /// <summary>사용 안 함.</summary>
    Off,
}

/// <summary>동영상 나누기 방식.</summary>
public enum SplitMethod
{
    /// <summary>지정한 개수로 균등 분할.</summary>
    ByCount,
    /// <summary>지정한 시간 단위로 분할.</summary>
    ByDuration,
}
