using System.Globalization;

namespace VCut.App;

/// <summary>변환 모드 상세의 숫자 입력 텍스트박스(가로/세로/비트레이트/프레임레이트 등)가
/// 공유하는 표시/파싱 규칙 — 재생 속도 텍스트박스와 동일한 방식.</summary>
public static class NumericFormat
{
    public static string Text(double v, int decimals) =>
        v.ToString(decimals > 0 ? "0." + new string('0', decimals) : "0", CultureInfo.InvariantCulture);

    /// <summary>텍스트를 숫자로 파싱해 [min,max]로 clamp. 실패하면 fallback(보통 변경 전 값) 유지.</summary>
    public static double Parse(string? text, double fallback, double min, double max, int decimals)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Round(Math.Clamp(v, min, max), decimals);
        return fallback;
    }
}
