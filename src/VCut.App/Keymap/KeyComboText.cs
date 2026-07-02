using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace VCut.App.Keymap;

/// <summary>단축키 조합(VirtualKey + 수식키) ↔ "Ctrl+Shift+S" 형식 문자열 변환 및 현재 키 상태 조회.</summary>
public static class KeyComboText
{
    /// <summary>현재 눌려 있는 수식키(Ctrl/Shift/Alt/Win) 상태를 조회.</summary>
    public static VirtualKeyModifiers CurrentModifiers()
    {
        var mods = VirtualKeyModifiers.None;
        if (IsDown(VirtualKey.Control)) mods |= VirtualKeyModifiers.Control;
        if (IsDown(VirtualKey.Shift)) mods |= VirtualKeyModifiers.Shift;
        if (IsDown(VirtualKey.Menu)) mods |= VirtualKeyModifiers.Menu;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) mods |= VirtualKeyModifiers.Windows;
        return mods;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    public static bool IsModifierKey(VirtualKey key) => key is
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl or
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift or
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu or
        VirtualKey.LeftWindows or VirtualKey.RightWindows;

    public static string Format(VirtualKey key, VirtualKeyModifiers mods)
    {
        if (key == VirtualKey.None) return "";
        var parts = new List<string>();
        if (mods.HasFlag(VirtualKeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(VirtualKeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(VirtualKeyModifiers.Menu)) parts.Add("Alt");
        if (mods.HasFlag(VirtualKeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    // WinUI의 VirtualKey 열거형에는 '['(VK_OEM_4=219)/']'(VK_OEM_6=221) 같은 OEM 문장부호 키에
    // 대응하는 명명된 멤버가 없어 숫자값으로만 표현되므로, 표시용 별칭을 직접 매핑한다.
    private const int VkOem4 = 219; // '['
    private const int VkOem6 = 221; // ']'

    private static string KeyName(VirtualKey key) => (int)key switch
    {
        VkOem4 => "[",
        VkOem6 => "]",
        _ when key is >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)key - (int)VirtualKey.Number0).ToString(),
        _ => key.ToString(),
    };
}
