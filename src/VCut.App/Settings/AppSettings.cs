using VCut.Core.Models;

namespace VCut.App.Settings;

/// <summary>앱 테마.</summary>
public enum AppTheme { Dark, Light }

/// <summary>저장 폴더 결정 방식.</summary>
public enum SaveFolderMode
{
    /// <summary>원본 파일과 같은 폴더에 저장.</summary>
    SameAsSource,
    /// <summary>지정한 폴더에 저장.</summary>
    Custom,
}

/// <summary>
/// 앱 환경설정. docx '환경 설정'의 일반/재생/파일/언어/고속 모드 항목에 대응.
/// 실제 동작에 영향을 주는 항목은 엔진/앱 흐름에 연결되고, 나머지는 상태만 보존.
/// </summary>
public sealed class AppSettings
{
    // ── 일반 ──
    public bool WarnUnseekable { get; set; } = true;
    public bool WarnFileExists { get; set; } = true;
    public bool ShowProjectSaveMessage { get; set; } = true;
    /// <summary>[연결] 작업 완료 후 저장 폴더를 탐색기로 열기.</summary>
    public bool OpenFolderAfterDone { get; set; } = true;
    /// <summary>[연결] 출력설정 로그(.log) 생성.</summary>
    public bool CreateLogFile { get; set; }
    /// <summary>[연결] MP4 MOOV를 앞부분에 저장(faststart).</summary>
    public bool MoovAtFront { get; set; }
    public bool KeepCreationTime { get; set; }
    public bool ShowTips { get; set; } = true;
    public bool AutoAdvanceCursor { get; set; } = true;

    // ── 재생 ──
    public bool WarnUnplayable { get; set; } = true;
    public bool DeinterlaceOnPlay { get; set; }
    public bool HardwareRenderer { get; set; } = true;
    public bool UseHardwareDecoder { get; set; } = true;

    // ── 파일 ──
    /// <summary>[연결] 저장 폴더 방식.</summary>
    public SaveFolderMode SaveFolderMode { get; set; } = SaveFolderMode.SameAsSource;
    /// <summary>[연결] 지정 저장 폴더(SaveFolderMode=Custom일 때).</summary>
    public string SaveFolder { get; set; } = "";
    /// <summary>임시 파일 폴더(Cache). 비우면 시스템 임시 폴더 사용.</summary>
    public string TempFolder { get; set; } = "";

    // ── 언어 ──
    /// <summary>UI 언어 코드(ko/en/ja…). 현재는 보존만.</summary>
    public string Language { get; set; } = "ko";

    // ── 고속 모드 ──
    public bool WarnFastUnavailable { get; set; } = true;
    /// <summary>[연결] 항상 키프레임 단위로 고속 모드 사용.</summary>
    public bool AlwaysKeyframe { get; set; }
    public bool WarnStartShift { get; set; } = true;
    public bool RelaxFastMerge { get; set; }

    // ── 코덱(엔진 기본값) ──
    /// <summary>[연결] 변환 시 기본 하드웨어 가속 인코더.</summary>
    public HardwareAccel DefaultHardwareAccel { get; set; } = HardwareAccel.None;

    // ── 테마 ──
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>지정한 원본 기준으로 실제 출력 폴더를 결정. null이면 호출측에서 원본 폴더 사용.</summary>
    public string? ResolveOutputDir(string sourcePath)
    {
        if (SaveFolderMode == SaveFolderMode.Custom &&
            !string.IsNullOrWhiteSpace(SaveFolder) &&
            Directory.Exists(SaveFolder))
            return SaveFolder;
        return null; // null → 원본과 같은 폴더(OutputNaming 기본 동작)
    }
}
