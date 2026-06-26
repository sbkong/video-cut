<#
.SYNOPSIS
  v-cut을 self-contained로 publish하고, ffmpeg 동봉(옵션) 후 Portable ZIP을 생성한다.

.PARAMETER FfmpegDir
  ffmpeg.exe / ffprobe.exe (가능하면 static 빌드)가 있는 폴더. 지정 시 dist\app\ffmpeg 에 동봉.
  생략하면 installer\ffmpeg 폴더가 있으면 사용하고, 둘 다 없으면 대상 PC의 PATH에 의존.

.PARAMETER Configuration
  빌드 구성(기본 Release).

.EXAMPLE
  pwsh build\publish.ps1
  pwsh build\publish.ps1 -FfmpegDir "C:\ffmpeg\bin"
#>
param(
    [string]$FfmpegDir = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$appDir = Join-Path $dist "app"

Write-Host "== 1) 이전 산출물 정리 ==" -ForegroundColor Cyan
if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "== 2) self-contained publish ==" -ForegroundColor Cyan
& dotnet publish (Join-Path $root "src\VCut.App\VCut.App.csproj") `
    -c $Configuration -r win-x64 -p:Platform=x64 -o $appDir --nologo
if ($LASTEXITCODE -ne 0) { throw "publish 실패" }

Write-Host "== 3) ffmpeg 동봉 ==" -ForegroundColor Cyan
$srcFfmpeg = ""
if ($FfmpegDir -and (Test-Path $FfmpegDir)) {
    $srcFfmpeg = $FfmpegDir
} elseif (Test-Path (Join-Path $root "installer\ffmpeg")) {
    $srcFfmpeg = Join-Path $root "installer\ffmpeg"
}

if ($srcFfmpeg) {
    $ffDest = Join-Path $appDir "ffmpeg"
    New-Item -ItemType Directory -Force -Path $ffDest | Out-Null
    # ffmpeg.exe/ffprobe.exe 와 같은 폴더의 DLL(공유 빌드 의존성)을 함께 복사.
    Get-ChildItem $srcFfmpeg -Include "ffmpeg.exe","ffprobe.exe","*.dll" -File -ErrorAction SilentlyContinue |
        ForEach-Object { Copy-Item $_.FullName $ffDest -Force }
    Write-Host "   ffmpeg 동봉: $srcFfmpeg -> $ffDest" -ForegroundColor Green
} else {
    Write-Warning "   ffmpeg 미동봉 — 대상 PC의 PATH에 ffmpeg가 있어야 동작합니다."
    Write-Warning "   배포용으로는 static ffmpeg(ffmpeg.exe/ffprobe.exe)를 installer\ffmpeg 에 두거나 -FfmpegDir 로 지정하세요."
}

Write-Host "== 4) Portable ZIP 생성 ==" -ForegroundColor Cyan
$zip = Join-Path $dist "v-cut-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $appDir "*") -DestinationPath $zip
$zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "   $zip ($zipMb MB)" -ForegroundColor Green

Write-Host "`n완료. setup.exe는 build\make-installer.ps1 로 생성하세요." -ForegroundColor Cyan
