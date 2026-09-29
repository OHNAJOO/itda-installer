; 잇다 설치 프로그램 (Inno Setup 6). 직접 컴파일하지 말고 build.ps1을 쓴다 (아래 값을 /D로 넘김).
;
; .gguf(약 5.3GB) 때문에 설치 데이터가 Inno의 한 파일 한계(2,100,000,000바이트)를 넘으므로 DiskSpanning으로
; ITDA-Setup-x.y.z.exe + ITDA-Setup-x.y.z-N.bin 여러 개를 만든다. 사용자에게는 이 파일들을 한 폴더(zip)로 전달한다.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppPublisher
  #define AppPublisher "ITDA"
#endif
#ifndef StagingDir
  #define StagingDir "..\build\staging"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifndef LangFile
  #define LangFile "compiler:Languages\Korean.isl"
#endif
; settings.yaml의 model_name. 이미 Ollama에 등록되어 있으면 재설치 때 .gguf 복사를 건너뛴다.
#ifndef ModelName
  #error ModelName을 /DModelName=... 으로 넘겨야 합니다 (build.ps1 사용)
#endif
#ifndef OllamaSetupFile
  #error OllamaSetupFile을 /DOllamaSetupFile=... 으로 넘겨야 합니다 (build.ps1 사용)
#endif
; GgufFile이 없으면 모델 없는 빌드(build.ps1 -NoModel): 앱만 업데이트할 때 쓴다.

#define AppExe "ITDA.exe"
#define AppDisplayName "잇다"

[Setup]
; AppId는 절대 바꾸지 않는다 (바꾸면 업데이트가 아니라 별도 프로그램으로 설치됨)
AppId={{93B97B6A-3634-48C4-90EB-52D789B3205D}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppDisplayName}
VersionInfoDescription={#AppDisplayName} 설치 프로그램

; 사용자 권한 설치 (관리자 불필요): %LOCALAPPDATA%\Programs\ITDA
PrivilegesRequired=lowest
DefaultDirName={userpf}\ITDA
DisableDirPage=auto
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

WizardStyle=modern
SetupIconFile=..\assets\generated\itda.ico
WizardImageFile=..\assets\generated\wizard-large-100.bmp,..\assets\generated\wizard-large-150.bmp,..\assets\generated\wizard-large-200.bmp
WizardSmallImageFile=..\assets\generated\wizard-small-100.bmp,..\assets\generated\wizard-small-150.bmp,..\assets\generated\wizard-small-200.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppDisplayName}

OutputDir={#OutputDir}
OutputBaseFilename=ITDA-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; 설치 데이터를 exe + .bin 조각으로 나눈다. 조각 하나를 2,000,000,000바이트로 고정한다 (FAT32 USB의 4GB 파일 한도 아래, Inno 6.7의 max는 이보다 훨씬 커서 쓰지 않음).
DiskSpanning=yes
DiskSliceSize=2000000000
; 설치 전에 [Code]에서 실행 중인 잇다를 직접 끄므로 Restart Manager 안내 창은 띄우지 않는다
CloseApplications=no

[Languages]
Name: "korean"; MessagesFile: "{#LangFile}"

[Messages]
; DiskSpanning: .bin을 못 찾으면 나오는 "디스크를 넣으세요" 창. 대부분 zip 안에서 바로 실행한 경우다.
SelectDiskLabel2=설치 파일 일부(%1번째 .bin 파일)를 찾을 수 없습니다.%n%nzip 파일 안에서 바로 실행했다면, 먼저 압축을 모두 푼 뒤 풀린 폴더에서 ITDA-Setup 파일을 다시 실행해 주세요.%n%n.bin 파일이 다른 폴더에 있다면 [찾아보기]로 그 폴더를 골라 주세요.

[CustomMessages]
LaunchNow=지금 잇다 실행하기
InstallingOllama=AI 엔진(Ollama)을 설치하는 중입니다. 몇 분 걸릴 수 있습니다...
DeleteDataQuestion=잇다에 저장한 기록(메모·일정)과 로그도 삭제할까요?%n%n[예]를 누르면 기록이 영구히 지워지며 되돌릴 수 없습니다.%n[아니요]를 누르면 기록을 남겨 두고, 나중에 다시 설치하면 그대로 이어서 쓸 수 있습니다.%n%n(AI 엔진 Ollama와 등록된 AI 모델은 지우지 않습니다.)

[InstallDelete]
; 업데이트 때 이전 버전의 파일(삭제된 모듈 등)이 남지 않도록 통째로 교체
Type: filesandordirs; Name: "{app}\backend"
Type: filesandordirs; Name: "{app}\python"

[Files]
Source: "{#StagingDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\launcher.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\Binggrae.ttf"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StagingDir}\python\*"; DestDir: "{app}\python"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StagingDir}\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StagingDir}\model\Modelfile"; DestDir: "{app}\model"; Flags: ignoreversion
#ifdef GgufFile
; 이미 압축된 데이터라 압축하지 않는다. 모델이 이미 Ollama에 등록되어 있으면 복사하지 않는다.
Source: "{#GgufFile}"; DestDir: "{app}\model"; Flags: ignoreversion nocompression; Check: NeedModelFile
#endif
; Ollama가 없을 때만 임시 폴더에 풀어 설치하고 지운다 (설치 폴더에 남기지 않음)
Source: "{#OllamaSetupFile}"; DestDir: "{tmp}"; DestName: "OllamaSetup.exe"; Flags: nocompression deleteafterinstall; Check: not IsOllamaInstalled

[Icons]
Name: "{userprograms}\{#AppDisplayName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppDisplayName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"

[Run]
; Ollama도 사용자 권한 설치 (%LOCALAPPDATA%\Programs\Ollama). 설치 뒤 Ollama 트레이 앱이 스스로 켜질 수 있으며,
; 런처는 11434 포트 응답으로 판단하므로 중복으로 띄우지 않는다.
Filename: "{tmp}\OllamaSetup.exe"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"; StatusMsg: "{cm:InstallingOllama}"; Flags: waituntilterminated; Check: not IsOllamaInstalled
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 실행 중 생긴 파일(__pycache__, 등록 뒤 남은 모델 파일 등)까지 지운다. 사용자 데이터(%LOCALAPPDATA%\ITDA)는 [Code]에서 묻는다.
Type: filesandordirs; Name: "{app}\backend"
Type: filesandordirs; Name: "{app}\python"
Type: filesandordirs; Name: "{app}\model"
Type: dirifempty; Name: "{app}"

[Code]
var
  DeleteUserData: Boolean;
  OllamaInstalledChecked: Boolean;
  OllamaInstalledCache: Boolean;
  ModelCheckDone: Boolean;
  ModelNeededCache: Boolean;

{ Ollama가 이미 설치되어 있는지: 기본 설치 위치와 PATH }
function IsOllamaInstalled: Boolean;
begin
  if not OllamaInstalledChecked then
  begin
    OllamaInstalledCache :=
      FileExists(ExpandConstant('{localappdata}\Programs\Ollama\ollama.exe')) or
      FileExists(ExpandConstant('{commonpf64}\Ollama\ollama.exe')) or
      (FileSearch('ollama.exe', GetEnv('PATH')) <> '');
    OllamaInstalledChecked := True;
    Log('Ollama 설치됨: ' + IntToStr(Ord(OllamaInstalledCache)));
  end;
  Result := OllamaInstalledCache;
end;

{ 실행 중인 Ollama에 모델 목록(/api/tags)을 묻는다. 응답이 오면 Answered = True. }
function OllamaApiHasModel(var Answered: Boolean): Boolean;
var
  Http: Variant;
  Body: String;
begin
  Answered := False;
  Result := False;
  try
    Http := CreateOleObject('WinHttp.WinHttpRequest.5.1');
    Http.SetTimeouts(2000, 2000, 2000, 5000);
    Http.Open('GET', 'http://127.0.0.1:11434/api/tags', False);
    Http.Send('');
    if Http.Status = 200 then
    begin
      Body := Http.ResponseText;
      Answered := True;
      { 런처와 같은 기준: "<이름>" 또는 "<이름>:latest" }
      Result := (Pos('"{#ModelName}"', Body) > 0) or (Pos('"{#ModelName}:latest"', Body) > 0);
    end;
  except
    Log('Ollama API 응답 없음: ' + GetExceptionMessage);
  end;
end;

{ 모델이 Ollama에 이미 등록되어 있으면 .gguf를 복사하지 않는다 (재설치·업데이트 때 5GB 복사를 아낌).
  Ollama가 실행 중이면 API 답을 따르고(모델 폴더 위치와 상관없이 정확), 꺼져 있으면
  <models>\manifests\registry.ollama.ai\library\<이름>\latest 파일로 판단한다. }
function NeedModelFile: Boolean;
var
  ModelsDir, Manifest: String;
  Answered, Registered: Boolean;
begin
  if not ModelCheckDone then
  begin
    Registered := OllamaApiHasModel(Answered);
    if Answered then
      Log('Ollama API 기준 모델 등록됨: ' + IntToStr(Ord(Registered)))
    else
    begin
      ModelsDir := GetEnv('OLLAMA_MODELS');
      if ModelsDir = '' then
        ModelsDir := ExpandConstant('{%USERPROFILE}\.ollama\models');
      Manifest := AddBackslash(ModelsDir) + 'manifests\registry.ollama.ai\library\{#ModelName}\latest';
      Registered := FileExists(Manifest);
      Log('모델 manifest ' + Manifest + ' 있음: ' + IntToStr(Ord(Registered)));
    end;
    ModelNeededCache := not Registered;
    ModelCheckDone := True;
    if Registered then
      Log('모델이 이미 등록되어 있어 모델 파일 복사를 건너뜁니다.');
  end;
  Result := ModelNeededCache;
end;

{ 설치 폴더 안의 실행 파일(ITDA.exe, 서버 python.exe)을 모두 끈다.
  ITDA.exe가 끝나면 Job Object 때문에 런처가 띄운 서버·ollama serve도 함께 끝난다.
  사용자가 따로 쓰는 python이나 Ollama는 경로가 달라 건드리지 않는다. }
procedure StopRunningApp(AppDir: String);
var
  ResultCode: Integer;
  Cmd: String;
begin
  if not DirExists(AppDir) then Exit;
  Cmd := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' +
    '$d = ''' + AddBackslash(AppDir) + '''; ' +
    'Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($d, [StringComparison]::OrdinalIgnoreCase) } | Stop-Process -Force; ' +
    'Start-Sleep -Milliseconds 800"';
  Log('실행 중인 잇다 종료: ' + AppDir);
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp(ExpandConstant('{app}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  case CurUninstallStep of
    usUninstall:
      begin
        { 제거 확인 뒤, 파일을 지우기 전: 사용자 데이터 삭제 여부를 묻고 실행 중인 잇다를 끈다 }
        DeleteUserData := False;
        if not UninstallSilent then
          DeleteUserData := MsgBox(CustomMessage('DeleteDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
        StopRunningApp(ExpandConstant('{app}'));
      end;
    usPostUninstall:
      if DeleteUserData then
      begin
        Log('사용자 데이터 삭제: ' + ExpandConstant('{localappdata}\ITDA'));
        DelTree(ExpandConstant('{localappdata}\ITDA'), True, True, True);
      end;
  end;
end;
