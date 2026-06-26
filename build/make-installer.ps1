<#
.SYNOPSIS
  Inno Setup(ISCC)로 dist\app 를 v-cut-setup.exe 로 패키징.
  먼저 build\publish.ps1 을 실행해 dist\app 가 준비돼 있어야 한다.
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$iss = Join-Path $root "installer\v-cut.iss"
$appDir = Join-Path $root "dist\app"

if (-not (Test-Path $appDir)) { throw "dist\app 가 없습니다. 먼저 build\publish.ps1 을 실행하세요." }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) { throw "ISCC.exe(Inno Setup 6)를 찾을 수 없습니다. https://jrsoftware.org 에서 설치하세요." }

Write-Host "ISCC: $iscc" -ForegroundColor Cyan
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC 실패" }
Write-Host "완료: dist\v-cut-setup.exe" -ForegroundColor Green
