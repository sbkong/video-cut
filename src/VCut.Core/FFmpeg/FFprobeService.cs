using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using VCut.Core.Models;

namespace VCut.Core.FFmpeg;

/// <summary>ffprobe로 동영상 파일을 분석해 <see cref="MediaInfo"/>를 생성.</summary>
public sealed class FFprobeService
{
    private readonly FFmpegLocator _locator;

    public FFprobeService(FFmpegLocator locator) => _locator = locator;

    public async Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("동영상 파일을 찾을 수 없습니다.", filePath);

        string[] args =
        [
            "-hide_banner", "-loglevel", "error",
            "-print_format", "json",
            "-show_format", "-show_streams",
            filePath,
        ];

        var psi = new ProcessStartInfo(_locator.FFprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.Start();
        var jsonTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        var json = await jsonTask.ConfigureAwait(false);
        var err = await errTask.ConfigureAwait(false);

        if (p.ExitCode != 0)
            throw new FFmpegException($"ffprobe 분석 실패: {err}", p.ExitCode, err);

        return Parse(filePath, json);
    }

    private static MediaInfo Parse(string filePath, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var videoStreams = new List<VideoStreamInfo>();
        var audioStreams = new List<AudioStreamInfo>();

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = GetString(s, "codec_type");
                if (type == "video")
                {
                    // 커버 아트 등 정지 이미지 스트림은 제외(disposition.attached_pic).
                    if (IsAttachedPicture(s)) continue;
                    videoStreams.Add(ParseVideo(s));
                }
                else if (type == "audio")
                {
                    audioStreams.Add(ParseAudio(s));
                }
            }
        }

        TimeSpan duration = TimeSpan.Zero;
        string formatName = "";
        long size = 0, bitrate = 0;
        if (root.TryGetProperty("format", out var fmt))
        {
            duration = ParseSeconds(GetString(fmt, "duration"));
            formatName = GetString(fmt, "format_name");
            size = ParseLong(GetString(fmt, "size"));
            bitrate = ParseLong(GetString(fmt, "bit_rate"));
        }

        // 컨테이너 duration이 없으면 비디오 스트림에서 추정.
        if (duration == TimeSpan.Zero && videoStreams.Count > 0 && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streams.EnumerateArray())
            {
                var d = ParseSeconds(GetString(s, "duration"));
                if (d > duration) duration = d;
            }
        }

        return new MediaInfo
        {
            FilePath = filePath,
            Duration = duration,
            FormatName = formatName,
            SizeBytes = size,
            BitRate = bitrate,
            VideoStreams = videoStreams,
            AudioStreams = audioStreams,
        };
    }

    private static VideoStreamInfo ParseVideo(JsonElement s)
    {
        bool? interlaced = null;
        var fieldOrder = GetString(s, "field_order");
        if (!string.IsNullOrEmpty(fieldOrder))
            interlaced = fieldOrder is "tt" or "bb" or "tb" or "bt";

        return new VideoStreamInfo
        {
            Index = GetInt(s, "index"),
            CodecName = GetString(s, "codec_name"),
            Width = GetInt(s, "width"),
            Height = GetInt(s, "height"),
            FrameRate = ParseFrameRate(GetString(s, "avg_frame_rate"), GetString(s, "r_frame_rate")),
            BitRate = ParseLong(GetString(s, "bit_rate")),
            PixelFormat = GetString(s, "pix_fmt"),
            IsInterlaced = interlaced,
            RotationDegrees = ParseRotation(s),
        };
    }

    private static AudioStreamInfo ParseAudio(JsonElement s) => new()
    {
        Index = GetInt(s, "index"),
        CodecName = GetString(s, "codec_name"),
        Channels = GetInt(s, "channels"),
        SampleRate = (int)ParseLong(GetString(s, "sample_rate")),
        BitRate = ParseLong(GetString(s, "bit_rate")),
        Language = GetTag(s, "language"),
    };

    private static bool IsAttachedPicture(JsonElement s)
    {
        if (s.TryGetProperty("disposition", out var d) &&
            d.TryGetProperty("attached_pic", out var ap) &&
            ap.ValueKind == JsonValueKind.Number)
            return ap.GetInt32() == 1;
        return false;
    }

    private static int ParseRotation(JsonElement s)
    {
        // 신형: side_data_list의 rotation, 구형: tags.rotate
        if (s.TryGetProperty("side_data_list", out var sd) && sd.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sd.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var r) && r.ValueKind == JsonValueKind.Number)
                {
                    int deg = ((int)Math.Round(r.GetDouble()) % 360 + 360) % 360;
                    return deg;
                }
            }
        }
        var tag = GetTag(s, "rotate");
        if (int.TryParse(tag, out var t)) return ((t % 360) + 360) % 360;
        return 0;
    }

    private static double ParseFrameRate(string avg, string r)
    {
        var v = ParseRational(avg);
        if (v <= 0) v = ParseRational(r);
        return v;
    }

    private static double ParseRational(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var parts = s.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
            d != 0)
            return n / d;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static TimeSpan ParseSeconds(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
            ? TimeSpan.FromSeconds(v)
            : TimeSpan.Zero;

    private static long ParseLong(string s) =>
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static string GetTag(JsonElement s, string tagName)
    {
        if (s.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in tags.EnumerateObject())
                if (string.Equals(prop.Name, tagName, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString() ?? "";
        }
        return "";
    }
}
