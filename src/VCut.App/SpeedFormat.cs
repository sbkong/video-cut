using System.Globalization;

namespace VCut.App;

/// <summary>배속 입력 텍스트박스의 표시/파싱 규칙(0.25~32, 소수점 2자리) — 여러 창에서 공유.</summary>
public static class SpeedFormat
{
    public const double Min = 0.25;
    public const double Max = 32;

    public static string Text(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>텍스트를 배속 값으로 파싱. 실패하면 fallback(보통 변경 전 값) 유지.</summary>
    public static double Parse(string? text, double fallback)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Round(Math.Clamp(v, Min, Max), 2);
        return fallback;
    }
}
