using VCut.Core.Models;

namespace VCut.App;

/// <summary>"포함할 항목" 표에 보여줄 컨테이너/비디오/오디오 정보 요약 문자열.</summary>
public static class MediaInfoFormat
{
    public static string Container(string filePath)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.');
        return ext.Length > 0 ? ext.ToUpperInvariant() : "-";
    }

    public static string Video(MediaInfo? info)
    {
        var v = info?.PrimaryVideo;
        return v is null ? "-" : $"{v.Resolution} · {v.FrameRate:0.##}fps · {v.CodecName}";
    }

    public static string Audio(MediaInfo? info)
    {
        var a = info?.PrimaryAudio;
        return a is null ? "오디오 없음" : $"{a.CodecName} · {a.ChannelLabel} · {a.SampleRate}Hz";
    }

    // ───────────────────────── 변환 모드 상세 — 원본 값 → UI 인덱스 매핑 ─────────────────────────
    // 콤보박스 항목 순서: 코덱 [H.264,HEVC,AV1,VP8,VP9,Xvid,MPEG-4,MotionJPEG] /
    // 오디오 코덱 [AAC,MP3,MP2,Opus,Vorbis,FLAC,PCM] / 채널 [원본,모노,스테레오,5.1,7.1] /
    // 샘플레이트 [원본,44100,48000,96000] — 아래 인덱스는 그 순서와 반드시 일치해야 함.

    public static int VideoCodecIndex(string? ffprobeCodecName) => ffprobeCodecName?.ToLowerInvariant() switch
    {
        "h264" => 0,
        "hevc" or "h265" => 1,
        "av1" => 2,
        "vp8" => 3,
        "vp9" => 4,
        "mpeg4" => 6,
        "mpeg1video" => 6,
        "mjpeg" => 7,
        _ => 0,
    };

    public static int AudioCodecIndex(string? ffprobeCodecName) => ffprobeCodecName?.ToLowerInvariant() switch
    {
        "aac" => 0,
        "mp3" => 1,
        "mp2" => 2,
        "opus" => 3,
        "vorbis" => 4,
        "flac" => 5,
        var p when p is not null && p.StartsWith("pcm") => 6,
        _ => 0,
    };

}
