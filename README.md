# v-cut

FFmpeg 기반 Windows 동영상 편집기. 단독 실행 앱(WinUI 3)이자, 핵심 엔진을 다른 .NET 프로그램에 모듈로 붙일 수 있는 라이브러리.

## 기술 스택

| 구분 | 기술 |
|------|------|
| 언어 / 런타임 | C# 12 / .NET 8 |
| UI | WinUI 3 (Windows App SDK 1.5, Unpackaged) |
| 영상 처리 | FFmpeg (CLI 구동) |
| MVVM | CommunityToolkit.Mvvm |
| 설치 | (예정) WiX 5 |

## 프로젝트 구조

```
v-cut.sln
├─ src/VCut.Core        클래스 라이브러리 — FFmpeg 편집 엔진 (NuGet 배포 단위, UI 의존 없음)
├─ src/VCut.Controls    WinUI 3 컨트롤 — VideoPreview, TimeCodeBox, TimelineRangeBar
├─ src/VCut.App         WinUI 3 단독 실행 앱 (NavigationView 레이아웃 + 환경설정)
└─ src/VCut.Cli         엔진 검증/시연용 콘솔
```

## UI / 환경설정

- **레이아웃** — 좌측 모드 레일(홈/자르기/나누기/합치기/변환/배속/회전/mp3/일괄/도구) + 중앙 미리보기·타임라인 구간바·트랜스포트
- **타임라인** — 파란 시작/끝 핸들 + 주황 재생헤드(드래그·클릭 탐색)
- **구간 미세조정** — 시:분:초:프레임 위젯(휠 증감)
- **환경설정(F5)** — 일반/재생/파일/언어/고속 5섹션, `%LOCALAPPDATA%\v-cut\settings.json` 저장. 저장폴더·완료후 폴더열기·MOOV·기본 HW가속 등 엔진 연결
- **단축키** — F2/Ctrl+O 열기, F5 환경설정, Ctrl+S 프로젝트 저장

의존 방향: `VCut.App → VCut.Controls → VCut.Core`, `VCut.App → VCut.Core`

> 모듈 통합 시: 영상 처리 로직만 필요하면 **VCut.Core**를, WinUI 3 앱에 미리보기 UI까지 넣으려면 **VCut.Controls**를 참조합니다. (App 프로젝트는 진입점이므로 직접 참조하지 않습니다.)

## 구현된 기능 (프로그램 사용방법.docx 기준)

- **자르기** — 구간 추출 (고속=스트림 복사 / 변환=재인코딩), 다중 구간 합치기
- **구간 제거** — 남길 앞·뒤 구간 자동 계산 후 합치기
- **나누기** — 개수(2~99) / 시간 단위 균등 분할
- **합치기** — concat (고속이 불가하면 변환 모드 자동 폴백)
- **mp3 추출** — 전체/구간 오디오 → MP3
- **배속** — 0.1x~99.9x (`setpts`+`atempo`), 4.01x↑ 오디오 자동 제거
- **변환 / 용량 줄이기** — 컨테이너·코덱·해상도·품질·FPS, 하드웨어 가속(NVENC/QSV/AMF), 디인터레이스, 노멀라이즈, faststart
- **회전 / 반전** — 90/180/270°, 좌우/상하
- **프레임 캡처** — 지정 시점 PNG
- **오디오 제거(고속)** — 비디오 재인코딩 없이 무음화
- **일괄 처리** — 여러 파일 일괄 변환/용량줄이기/mp3추출/오디오제거/배속
- **재생 시간 정보(.txt)** — 합치기 시 구간별 누적 시간(유튜브 챕터 형식)
- **편집 준비(remux)** — VOB/WebM 등 손상 타임스탬프 재설정
- **프로젝트 저장/열기(.vcproj)** — 파일 목록·구간·설정 보관
- **코덱** — H264·HEVC·AV1·VP8/9·XVID·MPEG1/4·MJPEG·YV12·RGB24

## 사전 요구사항

- .NET 8 SDK
- FFmpeg/ffprobe — 시스템 PATH에 있거나, 앱 실행 폴더(또는 `ffmpeg/` 하위)에 동봉

## 빌드 & 실행

```powershell
# 단독 실행 앱 (self-contained — Windows App Runtime 별도 설치 불필요)
# self-contained 모드는 명시적 플랫폼(x64)이 필요하므로 -p:Platform=x64 를 함께 지정
dotnet build src/VCut.App/VCut.App.csproj -c Debug -r win-x64 -p:Platform=x64
./src/VCut.App/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/v-cut.exe

# 엔진 검증용 CLI
dotnet run --project src/VCut.Cli -- probe <파일>
dotnet run --project src/VCut.Cli -- trim <파일> 00:00:02 00:00:05 convert
dotnet run --project src/VCut.Cli -- split <파일> count 3
dotnet run --project src/VCut.Cli -- merge out.mp4 a.mp4 b.mp4 fast
dotnet run --project src/VCut.Cli -- speed <파일> 2.0
dotnet run --project src/VCut.Cli -- convert <파일> webm
```

## 배포 패키지 만들기

```powershell
# 1) self-contained publish + Portable ZIP (dist\app, dist\v-cut-portable.zip)
pwsh build\publish.ps1
#    배포용 ffmpeg 동봉(권장): static 빌드 폴더 지정
pwsh build\publish.ps1 -FfmpegDir "C:\ffmpeg\bin"

# 2) 설치 프로그램(dist\v-cut-setup.exe) — Inno Setup 6 필요
pwsh build\make-installer.ps1
```

- **Portable ZIP** — 압축 해제 후 `v-cut.exe` 실행(설치 불필요). self-contained라 .NET/WinUI 런타임 동봉.
- **setup.exe** — 시작 메뉴/바탕화면 단축키 + 제거 프로그램 등록.
- **ffmpeg 동봉** — 미동봉 시 대상 PC의 PATH에 ffmpeg가 있어야 동작. 배포용으로는 static ffmpeg(`ffmpeg.exe`/`ffprobe.exe`)를 `installer\ffmpeg\`에 두거나 `-FfmpegDir`로 지정. `FFmpegLocator`가 앱 폴더의 `ffmpeg\` 하위를 자동 탐색.
- 코드 서명 인증서가 없으면 SmartScreen 경고가 표시될 수 있음(정상).

## 모듈로 사용하기 (다른 .NET 프로그램에서)

```csharp
using VCut.Core;
using VCut.Core.Models;

var editor = VideoEditor.Create();              // PATH 또는 동봉된 ffmpeg 자동 탐색
var info = await editor.ProbeAsync("input.mp4");

var settings = new ConversionSettings();
var result = await editor.TrimAsync(
    "input.mp4",
    [new MediaRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5))],
    join: false, OutputMode.Convert, settings);

Console.WriteLine(result.OutputFiles[0]);
```
