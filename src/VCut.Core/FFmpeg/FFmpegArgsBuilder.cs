using System.Globalization;
using VCut.Core.Models;

namespace VCut.Core.FFmpeg;

/// <summary>
/// 단일 입력 → 단일 출력 ffmpeg 명령을 구성. 자르기/변환/배속/회전/리사이즈/오디오 추출의 공통 기반.
/// 합치기·나누기는 별도 빌더에서 이 규칙을 재사용.
/// </summary>
public static class FFmpegArgsBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// 입력을 (선택 구간만) 출력으로 변환/복사하는 인자 목록을 생성.
    /// 모드가 Fast라도 회전·반전·배속 등 재인코딩이 필요한 설정이면 자동으로 변환 모드로 전환(docx 규칙).
    /// </summary>
    public static List<string> BuildTranscode(
        string input,
        MediaRange? range,
        OutputMode mode,
        ConversionSettings settings,
        string output)
    {
        bool fast = mode == OutputMode.Fast && !settings.RequiresReencode;

        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };

        // ── 입력 + 구간 ──
        if (range is { } r)
        {
            args.Add("-ss");
            args.Add(Sec(r.Start));
        }
        args.Add("-i");
        args.Add(input);
        if (range is { } r2)
        {
            args.Add("-t");
            args.Add(Sec(r2.Duration));
        }

        if (fast)
            BuildFastCopy(args, settings);
        else
            BuildReencode(args, settings);

        AddMuxFlags(args, settings, output);
        args.Add(output);
        return args;
    }

    // ───────────────────────── 고속 모드(스트림 복사) ─────────────────────────

    private static void BuildFastCopy(List<string> args, ConversionSettings s)
    {
        if (s.ExtractAudioOnly)
        {
            // 오디오만: 비디오 제거 후 오디오 스트림 복사.
            MapAudio(args, s);
            args.Add("-vn");
            args.Add("-c:a");
            args.Add("copy");
            return;
        }

        MapVideo(args, s);
        if (!s.RemoveAudio) MapAudio(args, s);

        args.Add("-c:v");
        args.Add("copy");
        if (s.RemoveAudio)
            args.Add("-an");
        else
        {
            args.Add("-c:a");
            args.Add("copy");
        }
    }

    // ───────────────────────── 변환 모드(재인코딩) ─────────────────────────

    private static void BuildReencode(List<string> args, ConversionSettings s)
    {
        // mp3 추출 등 오디오 전용 출력.
        if (s.ExtractAudioOnly)
        {
            MapAudio(args, s);
            args.Add("-vn");
            var af = BuildAudioFilters(s);
            if (af.Length > 0) { args.Add("-af"); args.Add(af); }
            AddAudioCodec(args, s, forceEncode: true);
            return;
        }

        MapVideo(args, s);
        bool keepAudio = !s.RemoveAudio && s.VideoCodec != VideoCodec.None;
        if (keepAudio) MapAudio(args, s);

        // ── 비디오 필터 + 코덱 ──
        if (s.VideoCodec == VideoCodec.None)
        {
            args.Add("-vn");
        }
        else
        {
            // 비디오 복사(원본 코덱 유지)는 필터를 적용할 수 없다(-vf + copy 충돌).
            if (s.VideoCodec != VideoCodec.Copy)
            {
                var vf = BuildVideoFilters(s);
                if (vf.Length > 0) { args.Add("-vf"); args.Add(vf); }
            }
            AddVideoCodec(args, s);
        }

        // ── 오디오 필터 + 코덱 ──
        if (!keepAudio)
        {
            args.Add("-an");
        }
        else
        {
            var af = BuildAudioFilters(s);
            if (af.Length > 0) { args.Add("-af"); args.Add(af); }
            AddAudioCodec(args, s, forceEncode: false);
        }
    }

    // ───────────────────────── 스트림 매핑 ─────────────────────────

    private static void MapVideo(List<string> args, ConversionSettings s)
    {
        args.Add("-map");
        args.Add(s.SelectedVideoStreamIndex is { } vi ? $"0:{vi}" : "0:v:0?");
    }

    private static void MapAudio(List<string> args, ConversionSettings s)
    {
        args.Add("-map");
        args.Add(s.SelectedAudioStreamIndex is { } ai ? $"0:{ai}" : "0:a:0?");
    }

    // ───────────────────────── 비디오 필터 체인 ─────────────────────────

    internal static string BuildVideoFilters(ConversionSettings s)
    {
        var f = new List<string>();

        // 1) 디인터레이스
        switch (s.Deinterlace)
        {
            case Deinterlace.Always: f.Add("yadif"); break;
            case Deinterlace.Auto: f.Add("yadif=deint=interlaced"); break;
            case Deinterlace.Off: break;
        }

        // 2) 크기 조정 (-2 = 비율 유지 + 짝수 보정)
        switch (s.SizeMode)
        {
            case VideoSizeMode.FitWidth when s.Width > 0:
                f.Add($"scale={s.Width}:-2"); break;
            case VideoSizeMode.FitHeight when s.Height > 0:
                f.Add($"scale=-2:{s.Height}"); break;
            case VideoSizeMode.Fixed when s.Width > 0 && s.Height > 0:
                f.Add($"scale={s.Width}:{s.Height}"); break;
        }

        // 3) 회전
        switch (s.Rotation)
        {
            case Rotation.R90: f.Add("transpose=1"); break;
            case Rotation.R270: f.Add("transpose=2"); break;
            case Rotation.R180: f.Add("transpose=2,transpose=2"); break;
        }

        // 4) 반전
        if (s.FlipHorizontal) f.Add("hflip");
        if (s.FlipVertical) f.Add("vflip");

        // 5) 프레임레이트
        if (s.FrameRate > 0)
            f.Add($"fps={s.FrameRate.ToString("0.###", Inv)}");

        // 6) 배속(비디오 PTS 조정)
        if (Math.Abs(s.Speed - 1.0) > 0.001)
            f.Add($"setpts=PTS/{s.Speed.ToString("0.####", Inv)}");

        return string.Join(',', f);
    }

    // ───────────────────────── 오디오 필터 체인 ─────────────────────────

    internal static string BuildAudioFilters(ConversionSettings s)
    {
        var f = new List<string>();

        // 배속(atempo는 0.5~2.0 권장 → 범위 밖이면 체이닝)
        if (Math.Abs(s.Speed - 1.0) > 0.001)
            f.AddRange(BuildAtempoChain(s.Speed));

        // 노멀라이즈
        if (s.Normalize)
            f.Add("loudnorm=I=-16:TP=-1.5:LRA=11");

        return string.Join(',', f);
    }

    /// <summary>atempo는 인자당 0.5~2.0이 안전하므로 목표 배율을 그 범위의 곱으로 분해.</summary>
    internal static IEnumerable<string> BuildAtempoChain(double speed)
    {
        var chain = new List<string>();
        double remaining = speed;
        while (remaining > 2.0 + 1e-9)
        {
            chain.Add("atempo=2.0");
            remaining /= 2.0;
        }
        while (remaining < 0.5 - 1e-9)
        {
            chain.Add("atempo=0.5");
            remaining /= 0.5;
        }
        chain.Add($"atempo={remaining.ToString("0.######", Inv)}");
        return chain;
    }

    // ───────────────────────── 비디오 코덱 ─────────────────────────

    private static void AddVideoCodec(List<string> args, ConversionSettings s)
    {
        // 원본 코덱 유지 — 재인코딩 없이 비디오 스트림 복사.
        if (s.VideoCodec == VideoCodec.Copy)
        {
            args.Add("-c:v"); args.Add("copy");
            return;
        }

        var encoder = ResolveVideoEncoder(s.VideoCodec, s.HardwareAccel);
        args.Add("-c:v");
        args.Add(encoder);

        // 무압축 코덱: 비트레이트/품질 없이 픽셀 포맷만 지정.
        if (s.VideoCodec is VideoCodec.Yv12 or VideoCodec.Rgb24)
        {
            args.Add("-pix_fmt");
            args.Add(s.VideoCodec == VideoCodec.Yv12 ? "yuv420p" : "rgb24");
            return;
        }

        // 프로파일(H.264/HEVC)
        if (!string.IsNullOrEmpty(s.Profile) &&
            s.VideoCodec is VideoCodec.H264 or VideoCodec.Hevc)
        {
            args.Add("-profile:v");
            args.Add(s.Profile!);
        }

        // 품질/비트레이트
        if (s.VideoRateControl == RateControl.Cbr)
        {
            string br = s.VideoBitrateKbps + "k";
            args.Add("-b:v"); args.Add(br);
            args.Add("-maxrate"); args.Add(br);
            args.Add("-minrate"); args.Add(br);
            args.Add("-bufsize"); args.Add(s.VideoBitrateKbps * 2 + "k");
            if (s.HardwareAccel == HardwareAccel.Nvenc) { args.Add("-rc"); args.Add("cbr"); }
        }
        else
        {
            int crf = QualityToCrf(s.VideoQuality);
            switch (s.HardwareAccel)
            {
                case HardwareAccel.Nvenc:
                    args.Add("-rc"); args.Add("vbr");
                    args.Add("-cq"); args.Add(crf.ToString(Inv));
                    break;
                case HardwareAccel.Qsv:
                    args.Add("-global_quality"); args.Add(crf.ToString(Inv));
                    break;
                case HardwareAccel.Amf:
                    args.Add("-rc"); args.Add("cqp");
                    args.Add("-qp_i"); args.Add(crf.ToString(Inv));
                    args.Add("-qp_p"); args.Add(crf.ToString(Inv));
                    break;
                default:
                    // 소프트웨어 인코더는 코덱별로 품질 옵션이 다름.
                    AddSoftwareQuality(args, s.VideoCodec, crf, s.VideoQuality);
                    break;
            }
        }

        // 호환 픽셀 포맷(8비트 4:2:0) — H.264/HEVC/MPEG4/XVID 기본.
        if (s.VideoCodec is VideoCodec.H264 or VideoCodec.Hevc or VideoCodec.Mpeg4 or VideoCodec.Xvid
            && s.HardwareAccel == HardwareAccel.None)
        {
            args.Add("-pix_fmt"); args.Add("yuv420p");
        }
    }

    private static void AddSoftwareQuality(List<string> args, VideoCodec codec, int crf, int quality)
    {
        switch (codec)
        {
            case VideoCodec.H264:
            case VideoCodec.Hevc:
            case VideoCodec.Av1: // libsvtav1도 -crf 사용
                args.Add("-crf"); args.Add(crf.ToString(Inv));
                break;
            case VideoCodec.Vp8:
            case VideoCodec.Vp9:
                // libvpx: -crf + -b:v 0(품질 우선)
                args.Add("-crf"); args.Add(crf.ToString(Inv));
                args.Add("-b:v"); args.Add("0");
                break;
            case VideoCodec.Xvid:
            case VideoCodec.Mpeg4:
            case VideoCodec.Mpeg1:
                // -qscale:v 1(최고)~31(최저). 품질 0~100을 2~15에 매핑.
                int q = (int)Math.Clamp(Math.Round(15 - quality / 100.0 * 13), 2, 15);
                args.Add("-qscale:v"); args.Add(q.ToString(Inv));
                break;
            case VideoCodec.MotionJpeg:
                int mq = (int)Math.Clamp(Math.Round(15 - quality / 100.0 * 13), 2, 15);
                args.Add("-qscale:v"); args.Add(mq.ToString(Inv));
                break;
        }
    }

    internal static string ResolveVideoEncoder(VideoCodec codec, HardwareAccel hw) => codec switch
    {
        VideoCodec.Copy => "copy",
        VideoCodec.H264 => hw switch
        {
            HardwareAccel.Nvenc => "h264_nvenc",
            HardwareAccel.Qsv => "h264_qsv",
            HardwareAccel.Amf => "h264_amf",
            _ => "libx264",
        },
        VideoCodec.Hevc => hw switch
        {
            HardwareAccel.Nvenc => "hevc_nvenc",
            HardwareAccel.Qsv => "hevc_qsv",
            HardwareAccel.Amf => "hevc_amf",
            _ => "libx265",
        },
        VideoCodec.Av1 => hw switch
        {
            HardwareAccel.Nvenc => "av1_nvenc",
            HardwareAccel.Qsv => "av1_qsv",
            HardwareAccel.Amf => "av1_amf",
            _ => "libsvtav1",
        },
        VideoCodec.Vp8 => "libvpx",
        VideoCodec.Vp9 => "libvpx-vp9",
        VideoCodec.Xvid => "libxvid",
        VideoCodec.Mpeg1 => "mpeg1video",
        VideoCodec.Mpeg4 => "mpeg4",
        VideoCodec.MotionJpeg => "mjpeg",
        VideoCodec.Yv12 or VideoCodec.Rgb24 => "rawvideo",
        _ => "libx264",
    };

    /// <summary>품질 0~100 → CRF. 100=최고화질(낮은 CRF). 80≈18, 60≈23, 50≈26.</summary>
    internal static int QualityToCrf(int quality)
    {
        quality = Math.Clamp(quality, 0, 100);
        return (int)Math.Clamp(Math.Round(40 - quality * 0.28), 0, 51);
    }

    // ───────────────────────── 오디오 코덱 ─────────────────────────

    private static void AddAudioCodec(List<string> args, ConversionSettings s, bool forceEncode)
    {
        if (!forceEncode && s.AudioCodec == AudioCodec.Copy)
        {
            args.Add("-c:a"); args.Add("copy");
            return;
        }

        var (encoder, defaultForMp3) = ResolveAudioEncoder(s.AudioCodec, forceEncode);
        args.Add("-c:a"); args.Add(encoder);

        // 비트레이트/품질
        if (encoder is "pcm_s16le" or "flac")
        {
            // 무손실 — 비트레이트 설정 없음.
        }
        else if (defaultForMp3 && s.AudioRateControl == RateControl.Vbr)
        {
            // libmp3lame VBR 품질(0=최고~9). 192k≈-q:a 2 수준으로 비트레이트 지정이 더 직관적.
            args.Add("-b:a"); args.Add(s.AudioBitrateKbps + "k");
        }
        else
        {
            args.Add("-b:a"); args.Add(s.AudioBitrateKbps + "k");
        }

        if (s.AudioChannels > 0) { args.Add("-ac"); args.Add(s.AudioChannels.ToString(Inv)); }
        if (s.AudioSampleRate > 0) { args.Add("-ar"); args.Add(s.AudioSampleRate.ToString(Inv)); }
    }

    private static (string encoder, bool isMp3) ResolveAudioEncoder(AudioCodec codec, bool forceEncode)
    {
        // 오디오 전용(mp3 추출)에서 Copy가 들어오면 mp3로 강제.
        if (forceEncode && codec == AudioCodec.Copy)
            return ("libmp3lame", true);

        return codec switch
        {
            AudioCodec.Aac => ("aac", false),
            AudioCodec.Mp3 => ("libmp3lame", true),
            AudioCodec.Mp2 => ("mp2", false),
            AudioCodec.Pcm => ("pcm_s16le", false),
            AudioCodec.Opus => ("libopus", false),
            AudioCodec.Vorbis => ("libvorbis", false),
            AudioCodec.Flac => ("flac", false),
            _ => ("aac", false),
        };
    }

    // ───────────────────────── 먹싱 플래그 ─────────────────────────

    private static void AddMuxFlags(List<string> args, ConversionSettings s, string output)
    {
        // MP4 스트리밍 재생용 — MOOV를 앞으로(faststart). movflags는 MP4 계열에서만 유효하므로,
        // 실제 출력 확장자로 판정한다(고속모드는 스트림 복사라 출력 컨테이너가 설정과 다를 수 있음).
        if (s.MoovAtFront && !s.ExtractAudioOnly && IsMp4Container(output))
        {
            args.Add("-movflags"); args.Add("+faststart");
        }
    }

    /// <summary>MP4 계열(movflags 지원) 컨테이너인지 출력 확장자로 판정.</summary>
    internal static bool IsMp4Container(string path)
    {
        var e = Path.GetExtension(path);
        return e.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || e.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
            || e.Equals(".mov", StringComparison.OrdinalIgnoreCase);
    }

    // ───────────────────────── 헬퍼 ─────────────────────────

    /// <summary>TimeSpan을 ffmpeg용 초.밀리초 문자열로(불변 문화권).</summary>
    internal static string Sec(TimeSpan t) =>
        t.TotalSeconds.ToString("0.###", Inv);
}
