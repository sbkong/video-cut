using VCut.App.Settings;
using Windows.System;

namespace VCut.App.Keymap;

/// <summary>재할당 가능한 단축키 액션 하나의 정의(고정 메타데이터).</summary>
public sealed record KeymapActionDef(string Id, string LocKey, string CategoryLocKey, VirtualKey DefaultKey, VirtualKeyModifiers DefaultMods);

/// <summary>
/// 설정창 '단축키' 탭과 MainWindow의 실제 KeyboardAccelerator 등록에서 공유하는
/// 액션 목록·기본값·현재값 계산 로직.
/// </summary>
public static class KeymapActions
{
    /// <summary>'['(VK_OEM_4) / ']'(VK_OEM_6). VirtualKey에는 명명된 멤버가 없어 코드로 직접 지정.</summary>
    private const VirtualKey OemOpenBracket = (VirtualKey)219;
    private const VirtualKey OemCloseBracket = (VirtualKey)221;

    private const string CatFile     = "keymap.cat.file";
    private const string CatEdit     = "keymap.cat.edit";
    private const string CatPlayback = "keymap.cat.playback";

    public static readonly IReadOnlyList<KeymapActionDef> All =
    [
        new("open_file",       "keymap.action.open_file",       CatFile,     VirtualKey.O,      VirtualKeyModifiers.Control),
        new("save_project",    "keymap.action.save_project",    CatFile,     VirtualKey.S,      VirtualKeyModifiers.Control),
        new("save_project_as", "keymap.action.save_project_as", CatFile,     VirtualKey.S,      VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        new("open_project",    "keymap.action.open_project",    CatFile,     VirtualKey.P,      VirtualKeyModifiers.Control),
        new("open_settings",   "keymap.action.open_settings",   CatFile,     VirtualKey.F5,     VirtualKeyModifiers.None),

        new("remove_clip",     "keymap.action.remove_clip",     CatEdit,     VirtualKey.Delete, VirtualKeyModifiers.None),
        new("set_start",       "keymap.action.set_start",       CatEdit,     OemOpenBracket,    VirtualKeyModifiers.None),
        new("set_end",         "keymap.action.set_end",         CatEdit,     OemCloseBracket,   VirtualKeyModifiers.None),

        new("play_pause",      "keymap.action.play_pause",      CatPlayback, VirtualKey.Space,  VirtualKeyModifiers.None),
        new("seek_backward",   "keymap.action.seek_backward",   CatPlayback, VirtualKey.Left,   VirtualKeyModifiers.None),
        new("seek_forward",    "keymap.action.seek_forward",    CatPlayback, VirtualKey.Right,  VirtualKeyModifiers.None),
        new("prev_frame",      "keymap.action.prev_frame",      CatPlayback, VirtualKey.Left,   VirtualKeyModifiers.Control),
        new("next_frame",      "keymap.action.next_frame",      CatPlayback, VirtualKey.Right,  VirtualKeyModifiers.Control),
        new("prev_keyframe",   "keymap.action.prev_keyframe",   CatPlayback, VirtualKey.Left,   VirtualKeyModifiers.Menu),
        new("next_keyframe",   "keymap.action.next_keyframe",   CatPlayback, VirtualKey.Right,  VirtualKeyModifiers.Menu),
    ];

    /// <summary>액션의 현재 유효 단축키 문자열("Ctrl+O" 등). 단축키 없음이면 "".</summary>
    public static string ResolveCombo(AppSettings settings, string actionId)
    {
        if (settings.Keymap.TryGetValue(actionId, out var overridden)) return overridden;
        var def = All.FirstOrDefault(a => a.Id == actionId);
        return def is null ? "" : KeyComboText.Format(def.DefaultKey, def.DefaultMods);
    }

    /// <summary>주어진 단축키 조합을 이미 사용 중인 다른 액션이 있으면 그 ID를 반환.</summary>
    public static string? FindConflict(AppSettings settings, string combo, string excludeActionId)
    {
        if (string.IsNullOrEmpty(combo)) return null;
        foreach (var def in All)
        {
            if (def.Id == excludeActionId) continue;
            if (string.Equals(ResolveCombo(settings, def.Id), combo, StringComparison.OrdinalIgnoreCase))
                return def.Id;
        }
        return null;
    }
}
