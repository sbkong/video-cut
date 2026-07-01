using System.Text.Json;

namespace VCut.App.Locale;

/// <summary>
/// JSON 파일 기반 UI 문자열 현지화 서비스.
/// Assets/Locales/{languageCode}.json 파일을 로드하며,
/// 새 언어는 해당 파일을 추가하기만 하면 자동으로 지원됩니다.
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _strings = [];

    /// <summary>
    /// 앱 시작 시 한 번 호출합니다. SettingsStore.Load() 이후에 실행해야 합니다.
    /// 지정한 언어 파일이 없으면 ko.json으로 폴백합니다.
    /// </summary>
    public static void Load(string languageCode)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Assets", "Locales", $"{languageCode}.json");

        if (!File.Exists(path))
            path = Path.Combine(baseDir, "Assets", "Locales", "ko.json");

        if (!File.Exists(path))
        {
            _strings = [];
            return;
        }

        try
        {
            var json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch
        {
            _strings = [];
        }
    }

    /// <summary>키에 대응하는 번역 문자열을 반환합니다. 키가 없으면 키 자체를 반환합니다.</summary>
    public static string Get(string key) =>
        _strings.TryGetValue(key, out var v) ? v : key;

    /// <summary>번역 문자열에 {0}, {1}… 인수를 적용하여 반환합니다.</summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
