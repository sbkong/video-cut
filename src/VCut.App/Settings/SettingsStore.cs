using System.Text.Json;
using System.Text.Json.Serialization;

namespace VCut.App.Settings;

/// <summary>환경설정을 실행 파일과 같은 폴더의 settings.json에 저장/로드.</summary>
public static class SettingsStore
{
    private static readonly string FilePath =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>현재 적용 중인 설정(앱 시작 시 로드).</summary>
    public static AppSettings Current { get; private set; } = new();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
        return Current;
    }

    public static void Save(AppSettings settings)
    {
        Current = settings;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch { /* 저장 실패는 무시(권한 등) */ }
    }
}
