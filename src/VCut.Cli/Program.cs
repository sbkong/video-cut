using System.Globalization;
using VCut.Core;
using VCut.Core.FFmpeg;
using VCut.Core.Models;

// v-cut 엔진 검증/시연용 CLI.
// 사용법:
//   vcut probe   <file>
//   vcut trim    <file> <start> <end> [fast|convert]
//   vcut removeseg <file> <start> <end> [fast|convert]
//   vcut split   <file> count <n> | duration <sec> [fast|convert]
//   vcut merge   <out> <file1> <file2> [file3...] [fast|convert]
//   vcut mp3     <file> [start end]
//   vcut speed   <file> <factor>
//   vcut convert <file> <mp4|mkv|webm|avi> [width height]
//   vcut rotate  <file> <90|180|270> [hflip] [vflip]
//   vcut frame   <file> <seconds>

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

VideoEditor editor;
try
{
    editor = VideoEditor.Create();
}
catch (FFmpegException ex)
{
    Console.Error.WriteLine($"[오류] {ex.Message}");
    return 2;
}

var progress = new Progress<ProgressInfo>(p =>
{
    var eta = p.Eta is { } e ? $" ETA {e:mm\\:ss}" : "";
    Console.Write($"\r  진행 {p.Percent,3}%  {p.Speed:0.0}x{eta}        ");
});

void Done() => Console.WriteLine();

try
{
    var cmd = args[0].ToLowerInvariant();
    switch (cmd)
    {
        case "probe":
        {
            var info = await editor.ProbeAsync(args[1]);
            Console.WriteLine($"파일      : {info.FilePath}");
            Console.WriteLine($"포맷      : {info.FormatName}");
            Console.WriteLine($"길이      : {info.Duration:hh\\:mm\\:ss\\.fff}");
            Console.WriteLine($"크기      : {info.SizeBytes / 1024.0 / 1024.0:0.00} MB");
            Console.WriteLine($"전체비트레이트: {info.BitRate / 1000} kbps");
            foreach (var v in info.VideoStreams)
                Console.WriteLine($"  [V#{v.Index}] {v.CodecName} {v.Resolution} {v.FrameRate:0.##}fps " +
                                  $"{v.PixelFormat} rot={v.RotationDegrees} interlaced={v.IsInterlaced}");
            foreach (var a in info.AudioStreams)
                Console.WriteLine($"  [A#{a.Index}] {a.CodecName} {a.ChannelLabel} {a.SampleRate}Hz " +
                                  $"{a.BitRate / 1000}kbps lang={a.Language}");
            break;
        }

        case "trim":
        {
            var range = new MediaRange(ParseTime(args[2]), ParseTime(args[3]));
            var mode = ParseMode(args, 4);
            var r = await editor.TrimAsync(args[1], [range], join: false, mode, NewSettings(), progress: progress);
            Done(); Report(r);
            break;
        }

        case "removeseg":
        {
            var range = new MediaRange(ParseTime(args[2]), ParseTime(args[3]));
            var mode = ParseMode(args, 4);
            var r = await editor.RemoveSegmentsAsync(args[1], [range], mode, NewSettings(), progress: progress);
            Done(); Report(r);
            break;
        }

        case "split":
        {
            var mode = ParseMode(args, 4);
            EditResult r;
            if (args[2].Equals("count", StringComparison.OrdinalIgnoreCase))
                r = await editor.SplitAsync(args[1], SplitMethod.ByCount,
                    int.Parse(args[3]), TimeSpan.Zero, mode, NewSettings(), progress: progress);
            else
                r = await editor.SplitAsync(args[1], SplitMethod.ByDuration, 0,
                    TimeSpan.FromSeconds(double.Parse(args[3], CultureInfo.InvariantCulture)),
                    mode, NewSettings(), progress: progress);
            Done(); Report(r);
            break;
        }

        case "merge":
        {
            var outPath = args[1];
            bool wantInfo = args.Contains("info");
            var tokens = args.Skip(2).Where(a => a != "info").ToArray();
            var modeAtEnd = tokens.Length > 0 && tokens[^1] is "fast" or "convert";
            var mode = modeAtEnd && tokens[^1] == "convert" ? OutputMode.Convert : OutputMode.Fast;
            var inputs = tokens.Take(tokens.Length - (modeAtEnd ? 1 : 0)).ToArray();
            var settings = NewSettings();
            settings.WritePlaybackInfo = wantInfo;
            var r = await editor.MergeAsync(inputs, mode, settings, outPath, progress);
            Done(); Report(r);
            break;
        }

        case "mp3":
        {
            MediaRange? range = args.Length >= 4 ? new MediaRange(ParseTime(args[2]), ParseTime(args[3])) : null;
            var r = await editor.ExtractAudioAsync(args[1], range, progress: progress);
            Done(); Report(r);
            break;
        }

        case "speed":
        {
            var factor = double.Parse(args[2], CultureInfo.InvariantCulture);
            var r = await editor.ChangeSpeedAsync(args[1], factor, null, NewSettings(), progress: progress);
            Done(); Report(r);
            break;
        }

        case "convert":
        {
            var s = NewSettings();
            s.Container = args[2].ToLowerInvariant() switch
            {
                "mkv" => ContainerFormat.Mkv,
                "webm" => ContainerFormat.WebM,
                "avi" => ContainerFormat.Avi,
                _ => ContainerFormat.Mp4,
            };
            if (s.Container == ContainerFormat.WebM) { s.VideoCodec = VideoCodec.Vp9; s.AudioCodec = AudioCodec.Opus; }
            if (args.Length >= 5)
            {
                s.SizeMode = VideoSizeMode.Fixed;
                s.Width = int.Parse(args[3]);
                s.Height = int.Parse(args[4]);
            }
            var r = await editor.ConvertAsync(args[1], s, progress: progress);
            Done(); Report(r);
            break;
        }

        case "rotate":
        {
            var rot = int.Parse(args[2]) switch
            {
                90 => Rotation.R90, 180 => Rotation.R180, 270 => Rotation.R270, _ => Rotation.None
            };
            bool hflip = args.Contains("hflip");
            bool vflip = args.Contains("vflip");
            var r = await editor.RotateFlipAsync(args[1], rot, hflip, vflip, NewSettings(), progress: progress);
            Done(); Report(r);
            break;
        }

        case "frame":
        {
            var at = TimeSpan.FromSeconds(double.Parse(args[2], CultureInfo.InvariantCulture));
            var r = await editor.CaptureFrameAsync(args[1], at);
            Report(r);
            break;
        }

        case "mute":
        {
            var r = await editor.RemoveAudioAsync(args[1], progress: progress);
            Done(); Report(r);
            break;
        }

        case "prepare":
        {
            var r = await editor.PrepareForEditingAsync(args[1], progress: progress);
            Done(); Report(r);
            break;
        }

        case "batch":
        {
            // batch <convert|mp3|mute|speed> <arg> <file1> <file2> ...
            var sub = args[1].ToLowerInvariant();
            var files = args.Skip(sub == "speed" || sub == "convert" ? 3 : 2).ToArray();
            IReadOnlyList<EditResult> rs = sub switch
            {
                "mp3" => await editor.BatchExtractAudioAsync(files, progress: progress),
                "mute" => await editor.BatchRemoveAudioAsync(files, progress: progress),
                "speed" => await editor.BatchChangeSpeedAsync(files,
                    double.Parse(args[2], CultureInfo.InvariantCulture), NewSettings(), progress: progress),
                _ => await editor.BatchConvertAsync(files, ContainerSettings(args[2]), progress: progress),
            };
            Done();
            Console.WriteLine($"[일괄 완료] {rs.Count(x => x.Success)}/{rs.Count} 성공");
            foreach (var r in rs) Report(r);
            break;
        }

        case "project":
        {
            // project save <file.vcproj> <video> | project load <file.vcproj>
            if (args[1].Equals("save", StringComparison.OrdinalIgnoreCase))
            {
                var info = await editor.ProbeAsync(args[3]);
                var proj = new VCutProject
                {
                    FastMode = true,
                    Clips = [new ProjectClip
                    {
                        Path = Path.GetFullPath(args[3]),
                        Ranges = [ProjectRange.From(new MediaRange(TimeSpan.Zero, info.Duration))],
                    }],
                    Settings = ProjectSettings.From(NewSettings()),
                };
                await VCut.Core.Operations.ProjectFile.SaveAsync(proj, args[2]);
                Console.WriteLine($"[저장] {args[2]} (클립 {proj.Clips.Count}개)");
            }
            else
            {
                var proj = await VCut.Core.Operations.ProjectFile.LoadAsync(args[2]);
                Console.WriteLine($"[열기] 클립 {proj.Clips.Count}개, 고속모드={proj.FastMode}");
                foreach (var c in proj.Clips)
                    Console.WriteLine($"  {c.Path}  구간 {c.Ranges.Count}개");
            }
            break;
        }

        default:
            PrintUsage();
            return 1;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\n[오류] {ex.Message}");
    return 3;
}

static ConversionSettings NewSettings() => new();

static ConversionSettings ContainerSettings(string fmt)
{
    var s = new ConversionSettings();
    s.Container = fmt.ToLowerInvariant() switch
    {
        "mkv" => ContainerFormat.Mkv,
        "webm" => ContainerFormat.WebM,
        "avi" => ContainerFormat.Avi,
        _ => ContainerFormat.Mp4,
    };
    if (s.Container == ContainerFormat.WebM) { s.VideoCodec = VideoCodec.Vp9; s.AudioCodec = AudioCodec.Opus; }
    return s;
}

static OutputMode ParseMode(string[] args, int idx) =>
    idx < args.Length && args[idx].Equals("convert", StringComparison.OrdinalIgnoreCase)
        ? OutputMode.Convert : OutputMode.Fast;

static TimeSpan ParseTime(string s)
{
    // "90", "90.5", "00:01:30", "00:01:30.250" 모두 허용.
    if (s.Contains(':')) return TimeSpan.Parse(s, CultureInfo.InvariantCulture);
    return TimeSpan.FromSeconds(double.Parse(s, CultureInfo.InvariantCulture));
}

static void Report(EditResult r)
{
    if (r.Success)
    {
        Console.WriteLine($"[완료] {r.Elapsed.TotalSeconds:0.0}초");
        foreach (var f in r.OutputFiles)
        {
            var size = File.Exists(f) ? new FileInfo(f).Length / 1024.0 / 1024.0 : 0;
            Console.WriteLine($"  → {f}  ({size:0.00} MB)");
        }
    }
    else
    {
        Console.Error.WriteLine($"[실패] {r.ErrorMessage}");
        if (!string.IsNullOrEmpty(r.FFmpegLog))
            Console.Error.WriteLine($"  ffmpeg: {r.FFmpegLog}");
    }
}

static void PrintUsage()
{
    Console.WriteLine("""
        v-cut CLI (엔진 검증용)
          probe     <file>
          trim      <file> <start> <end> [fast|convert]
          removeseg <file> <start> <end> [fast|convert]
          split     <file> count <n> | duration <sec> [fast|convert]
          merge     <out> <file1> <file2> [...] [fast|convert]
          mp3       <file> [start end]
          speed     <file> <factor>
          convert   <file> <mp4|mkv|webm|avi> [width height]
          rotate    <file> <90|180|270> [hflip] [vflip]
          frame     <file> <seconds>
        시간 형식: 초(90, 90.5) 또는 hh:mm:ss(.fff)
        """);
}
