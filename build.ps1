<#
.SYNOPSIS
  잇다 Windows 설치 프로그램 빌드: staging 구성 → 런처 publish → Inno Setup 컴파일.

.EXAMPLE
  .\build.ps1 -Version 1.0.0
  .\build.ps1 -Version 1.0.1 -NoModel          # 모델 없이 앱만 (이미 모델이 등록된 PC 업데이트용)
  .\build.ps1 -Version 1.0.0 -Zip              # dist\ITDA-Setup-1.0.0.zip 까지 만듦

.NOTES
  이 파일은 UTF-8(BOM) 으로 저장해야 Windows PowerShell 5.1에서 한글이 깨지지 않는다.
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Publisher = "ITDA",
    # ../itda-release (읽기 전용으로만 사용)
    [string]$ReleaseDir = (Join-Path $PSScriptRoot "..\itda-release"),
    # python.org embeddable zip. 3.12는 3.12.10 이후 Windows 바이너리가 나오지 않으므로 3.12.10 고정
    [string]$PythonVersion = "3.12.10",
    # OllamaSetup.exe 경로. 비우면 build\cache에 받아 둔 것을 쓰고, 없으면 ollama.com에서 받는다.
    [string]$OllamaSetup = "",
    # 이보다 낮은 Ollama가 설치된 PC에서는 런처가 업데이트를 안내한다. 비우면 검사 안 함. 예: "0.12.0"
    [string]$MinOllamaVersion = "",
    # 모델 등록 뒤 설치 폴더의 .gguf를 남긴다 (기본: 지움)
    [switch]$KeepModelFile,
    # .gguf 없이 빌드 (DiskSpanning 없이도 되는 크기지만 설정은 그대로 둠)
    [switch]$NoModel,
    # 결과 폴더를 zip으로도 묶는다 (압축 없음 + zip64, 5GB 넘어도 됨)
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest 진행 표시가 매우 느려지는 문제 방지
Set-StrictMode -Version Latest

$Root = $PSScriptRoot
$BuildDir = Join-Path $Root "build"
$Cache = Join-Path $BuildDir "cache"
$Staging = Join-Path $BuildDir "staging"
$OutDir = Join-Path $Root "dist\ITDA-Setup-$Version"

function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Fail($msg) { Write-Host "오류: $msg" -ForegroundColor Red; exit 1 }
function Exec([string]$exe, [string[]]$argList) {
    & $exe @argList
    if ($LASTEXITCODE -ne 0) { Fail "$exe 실패 (종료 코드 $LASTEXITCODE)" }
}
function Write-Utf8Bom([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding $true))
}

# ---------------------------------------------------------------- 사전 준비물 확인
Step "사전 준비물 확인"
$ReleaseDir = (Resolve-Path $ReleaseDir -ErrorAction SilentlyContinue).Path
if (-not $ReleaseDir -or -not (Test-Path "$ReleaseDir\backend\app")) { Fail "itda-release 폴더를 찾을 수 없습니다 (-ReleaseDir 로 지정)" }
if (-not (Test-Path "$ReleaseDir\backend\static\index.html")) { Fail "$ReleaseDir\backend\static 에 화면 빌드 결과가 없습니다" }
$Wheels = "$ReleaseDir\wheels\win_amd64"
if (-not (Test-Path "$Wheels\requirements.txt")) { Fail "$Wheels\requirements.txt 가 없습니다" }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Fail ".NET 8 SDK(dotnet)가 필요합니다" }
if (-not (Get-Command py -ErrorAction SilentlyContinue)) { Fail "Python 런처(py)가 필요합니다 (python.org 설치본)" }
& py -3.12 -c "import sys" 2>$null
if ($LASTEXITCODE -ne 0) { Fail "Python 3.12가 필요합니다 (py -3.12 로 실행 가능해야 함)" }

$Iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) { $Iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $Iscc) { Fail "Inno Setup 6(ISCC.exe)을 찾을 수 없습니다" }

# 한국어 메시지 파일: Inno 설치본에 없으면 installer\Languages\Korean.isl 을 쓴다
$LangFile = Join-Path (Split-Path $Iscc) "Languages\Korean.isl"
if (-not (Test-Path $LangFile)) {
    $LangFile = Join-Path $Root "installer\Languages\Korean.isl"
    if (-not (Test-Path $LangFile)) {
        Fail ("Korean.isl 이 없습니다. https://jrsoftware.org/files/istrans/ 에서 Korean.isl 을 받아 " +
              "installer\Languages\Korean.isl 에 두세요")
    }
}

# 모델 정보: settings.yaml의 model_name, Modelfile의 FROM ./<gguf>
function First-Group([string]$path, [string]$pattern) {
    $m = Get-Content $path -Encoding UTF8 | Select-String $pattern | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value }
    return $null
}
$ModelName = First-Group "$ReleaseDir\backend\config\settings.yaml" '^model_name:\s*["'']?([^"''#\s]+)'
if (-not $ModelName) { Fail "settings.yaml 에서 model_name 을 찾을 수 없습니다" }
$GgufName = First-Group "$ReleaseDir\model\Modelfile" '^FROM\s+\./(\S+)\s*$'
if (-not $GgufName) { Fail "Modelfile 에서 FROM ./<gguf> 를 찾을 수 없습니다" }
$Gguf = "$ReleaseDir\model\$GgufName"
if (-not $NoModel -and -not (Test-Path $Gguf)) { Fail "모델 파일이 없습니다: $Gguf  (모델 없이 빌드하려면 -NoModel)" }
Write-Host "    모델 이름: $ModelName / 파일: $GgufName"

# ---------------------------------------------------------------- staging 초기화
Step "staging 초기화 ($Staging)"
New-Item -ItemType Directory -Force $Cache | Out-Null
if (Test-Path $Staging) { Remove-Item -Recurse -Force $Staging }
New-Item -ItemType Directory -Force $Staging, "$Staging\model" | Out-Null

# ---------------------------------------------------------------- backend
Step "backend 복사 (개발용 DB·캐시 제외)"
robocopy "$ReleaseDir\backend" "$Staging\backend" /E /NFL /NDL /NJH /NJS /NP /XF *.db *.db-journal *.db-wal *.db-shm /XD __pycache__ | Out-Null
if ($LASTEXITCODE -ge 8) { Fail "backend 복사 실패 (robocopy $LASTEXITCODE)" }
$global:LASTEXITCODE = 0
Remove-Item "$Staging\backend\requirements.txt" -ErrorAction SilentlyContinue
Copy-Item "$ReleaseDir\model\Modelfile" "$Staging\model\"

# ---------------------------------------------------------------- Python embeddable
Step "Python $PythonVersion embeddable"
$pyZip = "$Cache\python-$PythonVersion-embed-amd64.zip"
if (-not (Test-Path $pyZip)) {
    $url = "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip"
    Write-Host "    다운로드: $url"
    Invoke-WebRequest $url -OutFile "$pyZip.part" -UseBasicParsing
    Move-Item "$pyZip.part" $pyZip
}
Expand-Archive $pyZip "$Staging\python" -Force

# ._pth: site 활성화 + Lib\site-packages + backend (._pth가 있으면 PYTHONPATH·현재 폴더가 무시되므로 명시)
$pth = Get-ChildItem "$Staging\python\python3*._pth" | Select-Object -First 1
if (-not $pth) { Fail "embeddable 안에 ._pth 파일이 없습니다" }
$stdlibZip = (Get-ChildItem "$Staging\python\python3*.zip" | Select-Object -First 1).Name
Set-Content $pth.FullName -Encoding ASCII -Value @(
    $stdlibZip,
    ".",
    "Lib\site-packages",
    "..\backend",
    "import site"
)

# ---------------------------------------------------------------- 패키지 (오프라인 wheel)
Step "파이썬 패키지 설치 (wheels\win_amd64, 오프라인)"
$site = "$Staging\python\Lib\site-packages"
Exec py @("-3.12", "-m", "pip", "install", "--disable-pip-version-check", "--no-index",
    "--find-links", $Wheels, "-r", "$Wheels\requirements.txt", "--target", $site)
Remove-Item -Recurse -Force "$site\bin" -ErrorAction SilentlyContinue   # 콘솔 스크립트 (쓰지 않음)
Exec py @("-3.12", "-m", "compileall", "-q", "-j", "0", "$Staging\backend\app")

Step "동봉 Python으로 import 확인"
Push-Location "$Staging\backend"
try {
    & "$Staging\python\python.exe" -c "import fastapi, uvicorn, sqlalchemy, yaml, ollama, pydantic_core, httptools, websockets, watchfiles; import app.settings; print('ok', app.settings.settings()['model_name'])"
    if ($LASTEXITCODE -ne 0) { Fail "동봉 Python에서 패키지를 불러오지 못했습니다" }
} finally { Pop-Location }
Get-ChildItem "$Staging\backend" -Recurse -Filter *.db -ErrorAction SilentlyContinue | Remove-Item -Force

# ---------------------------------------------------------------- 런처
Step "런처 publish (.NET 8, self-contained 단일 파일)"
$pub = "$BuildDir\launcher-publish"
if (Test-Path $pub) { Remove-Item -Recurse -Force $pub }
Exec dotnet @("publish", "$Root\launcher\ITDA.Launcher.csproj", "-c", "Release", "-o", $pub,
    "-p:Version=$Version", "--nologo")
Copy-Item "$pub\ITDA.exe" $Staging
Copy-Item "$Root\assets\Binggrae.ttf" $Staging

$launcherConfig = [ordered]@{
    DeleteModelFileAfterRegister = (-not $KeepModelFile)
    MinOllamaVersion             = $MinOllamaVersion
    OllamaStartTimeoutSeconds    = 60
    ServerStartTimeoutSeconds    = 120
}
Write-Utf8Bom "$Staging\launcher.json" ($launcherConfig | ConvertTo-Json)

# ---------------------------------------------------------------- Ollama 설치 파일
Step "OllamaSetup.exe"
if (-not $OllamaSetup) {
    $OllamaSetup = "$Cache\OllamaSetup.exe"
    if (-not (Test-Path $OllamaSetup)) {
        Write-Host "    다운로드: https://ollama.com/download/OllamaSetup.exe"
        Invoke-WebRequest "https://ollama.com/download/OllamaSetup.exe" -OutFile "$OllamaSetup.part" -UseBasicParsing
        Move-Item "$OllamaSetup.part" $OllamaSetup
    }
}
if (-not (Test-Path $OllamaSetup)) { Fail "OllamaSetup.exe 를 찾을 수 없습니다: $OllamaSetup" }
$OllamaSetup = (Resolve-Path $OllamaSetup).Path
$ov = (Get-Item $OllamaSetup).VersionInfo.ProductVersion
Write-Host "    $OllamaSetup (버전 정보: $ov)"

# ---------------------------------------------------------------- Inno Setup
Step "Inno Setup 컴파일"
if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$isccArgs = @(
    "/Qp",
    "/DAppVersion=$Version",
    "/DAppPublisher=$Publisher",
    "/DStagingDir=$Staging",
    "/DOutputDir=$OutDir",
    "/DLangFile=$LangFile",
    "/DModelName=$ModelName",
    "/DOllamaSetupFile=$OllamaSetup"
)
if (-not $NoModel) { $isccArgs += "/DGgufFile=$Gguf" }
$isccArgs += "$Root\installer\itda.iss"
Exec $Iscc $isccArgs

Copy-Item "$Root\installer\README.txt" $OutDir

if ($Zip) {
    Step "zip 만들기"
    $zipPath = "$OutDir.zip"
    Remove-Item $zipPath -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # .bin 은 이미 압축된 데이터라 다시 압축하지 않는다. 폴더째 넣어 풀면 한 폴더에 모이게 한다.
    [IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath, [IO.Compression.CompressionLevel]::NoCompression, $true)
}

Step "완료"
Get-ChildItem $OutDir | ForEach-Object { "    {0,-40} {1,10:N0} MB" -f $_.Name, ($_.Length / 1MB) }
if ($Zip) { "    $OutDir.zip" }
Write-Host "배포: 위 폴더의 파일을 모두 함께 전달하세요 (exe와 .bin이 같은 폴더에 있어야 설치됩니다)."
