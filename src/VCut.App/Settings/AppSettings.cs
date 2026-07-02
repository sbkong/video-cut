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

/// <summary>작업 완료 후 폴더 열기 동작(캡처/일반 작업 공용).</summary>
public enum OpenFolderMode
{
    /// <summary>매번 물어보기.</summary>
    AlwaysAsk,
    /// <summary>항상 열기.</summary>
    AlwaysOpen,
    /// <summary>열지 않기.</summary>
    NeverOpen,
}

/// <summary>앱 기본 폰트 선택.</summary>
public enum FontChoice
{
    /// <summary>기본 내장 폰트: JetBrains Mono.</summary>
    JetBrainsMono,
    /// <summary>내장 폰트: 서울남산체.</summary>
    SeoulNamsan,
    /// <summary>시스템에 설치된 폰트 중 선택(SystemFontFamily).</summary>
    System,
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
    /// <summary>[연결] 자르기 등 일반 작업 완료 후 폴더 열기 동작(매번 묻기/항상 열기/열지 않기).</summary>
    public OpenFolderMode OutputOpenFolderMode { get; set; } = OpenFolderMode.AlwaysAsk;
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
    /// <summary>[연결] 동영상 저장 폴더 방식.</summary>
    public SaveFolderMode SaveFolderMode { get; set; } = SaveFolderMode.SameAsSource;
    /// <summary>[연결] 지정 동영상 저장 폴더(SaveFolderMode=Custom일 때).</summary>
    public string SaveFolder { get; set; } = "";
    /// <summary>[연결] 캡처 저장 폴더 방식.</summary>
    public SaveFolderMode CaptureFolderMode { get; set; } = SaveFolderMode.SameAsSource;
    /// <summary>[연결] 지정 캡처 저장 폴더(CaptureFolderMode=Custom일 때).</summary>
    public string CaptureFolder { get; set; } = "";
    /// <summary>[연결] 캡처 완료 후 폴더 열기 동작(매번 묻기/항상 열기/열지 않기).</summary>
    public OpenFolderMode CaptureOpenFolderMode { get; set; } = OpenFolderMode.AlwaysAsk;
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

    // ── 폰트 ──
    /// <summary>[연결] 앱 기본 폰트. 변경은 다음 실행부터 적용된다.</summary>
    public FontChoice Font { get; set; } = FontChoice.JetBrainsMono;
    /// <summary>Font=System일 때 사용할 시스템 폰트 패밀리 이름.</summary>
    public string SystemFontFamily { get; set; } = "Segoe UI";

    // ── 창 크기/위치 ──
    // 보조 모니터가 주 모니터의 좌/상단에 배치된 경우 WindowLeft/Top이 음수일 수 있으므로,
    // "저장된 값 없음"은 좌표 부호가 아니라 WindowPositionSet 플래그로 구분한다.
    public int WindowWidth { get; set; } = 1320;
    public int WindowHeight { get; set; } = 880;
    public int WindowLeft { get; set; }
    public int WindowTop { get; set; }
    public bool WindowPositionSet { get; set; }
    public bool WindowMaximized { get; set; } = false;

    // ── 최근 프로젝트 ──
    public List<string> RecentProjects { get; set; } = [];

    // ── 단축키 ──
    /// <summary>사용자 지정 단축키 재할당(액션ID → "Ctrl+Shift+S" 형식 문자열).
    /// 키가 없으면 기본 단축키 사용, 값이 빈 문자열이면 단축키 없음(명시적 해제).</summary>
    public Dictionary<string, string> Keymap { get; set; } = new();
    /// <summary>"N초 앞으로/뒤로" 단축키(←/→)가 한 번에 이동하는 초 단위 간격.</summary>
    public double SeekSeconds { get; set; } = 5;

    public AppSettings Clone()
    {
        var clone = (AppSettings)MemberwiseClone();
        clone.RecentProjects = [.. RecentProjects];
        clone.Keymap = new Dictionary<string, string>(Keymap);
        return clone;
    }

    /// <summary>지정한 원본 기준으로 실제 출력 폴더를 결정. null이면 호출측에서 원본 폴더 사용.</summary>
    public string? ResolveOutputDir(string sourcePath)
    {
        if (SaveFolderMode == SaveFolderMode.Custom &&
            !string.IsNullOrWhiteSpace(SaveFolder) &&
            Directory.Exists(SaveFolder))
            return SaveFolder;
        return null;
    }

    /// <summary>캡처 이미지 저장 폴더를 결정. null이면 원본 파일과 같은 폴더.</summary>
    public string? ResolveCaptureDir(string sourcePath)
    {
        if (CaptureFolderMode == SaveFolderMode.Custom &&
            !string.IsNullOrWhiteSpace(CaptureFolder) &&
            Directory.Exists(CaptureFolder))
            return CaptureFolder;
        return null;
    }
}
