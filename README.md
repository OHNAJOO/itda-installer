# itda-installer

잇다(ITDA)를 Windows 10/11 64비트에 설치하는 설치 프로그램과 실행기(런처)입니다. 사용자는 설치 파일을 실행하고, 이후에는 바탕화면 아이콘만 누르면 브라우저에서 잇다를 씁니다.

입력물은 옆 폴더의 `../itda-release`(읽기 전용)입니다. 이 레포는 그 내용을 Windows용으로 포장하기만 하며, 앱 코드는 수정하지 않습니다.

## 구성

```text
itda-installer/
├─ build.ps1                 # 빌드 전체: staging → 런처 publish → Inno Setup → dist\
├─ launcher/                 # 런처 ITDA.exe (C# .NET 8 WinForms 트레이 앱, self-contained 단일 파일)
├─ installer/
│  ├─ itda.iss               # Inno Setup 6 스크립트 (DiskSpanning, 사용자 권한 설치)
│  ├─ README.txt             # 배포 폴더에 같이 넣는 사용자 안내
│  └─ Languages/             # (선택) Inno 설치본에 Korean.isl이 없을 때 여기에 둠
├─ assets/
│  ├─ logo.png, mascot.png, Binggrae.ttf     # 원본
│  └─ generated/             # 아이콘(.ico)·마법사 이미지(.bmp). tools/make_assets.py로 만들고 커밋
├─ tools/make_assets.py      # 원본 이미지 → generated/ (원본을 바꿨을 때만 실행, Pillow 필요)
└─ TESTING.md                # 수동 테스트 체크리스트
```

## 사전 준비물 (Windows 개발 PC)

| 준비물 | 확인 방법 | 비고 |
| --- | --- | --- |
| Windows 10/11 64비트 | | 빌드는 Windows에서만 할 수 있습니다 (robocopy, ISCC, 동봉 Python 검사). |
| .NET 8 SDK | `dotnet --list-sdks` | 사용자 PC에는 .NET이 필요 없습니다. |
| Inno Setup 6 | `ISCC.exe` | 기본 설치 위치를 자동으로 찾습니다. 한국어 메시지 파일(`Languages\Korean.isl`)이 설치본에 없으면 [공식 번역](https://jrsoftware.org/files/istrans/)에서 받아 `installer\Languages\Korean.isl`에 둡니다. |
| Python 3.12 (python.org 설치본, `py` 런처 포함) | `py -3.12 --version` | 동봉할 패키지를 미리 설치하는 데만 씁니다. |
| `../itda-release` | `..\itda-release\backend\app` | `model\*.gguf`(약 5.3GB)는 git에 없으므로 따로 받아 둡니다. |
| 인터넷 (처음 한 번) | | Python embeddable zip과 OllamaSetup.exe를 `build\cache`에 받아 둡니다. |

## 빌드

```powershell
# PowerShell에서 (실행 정책 때문에 막히면: powershell -ExecutionPolicy Bypass -File .\build.ps1 ...)
.\build.ps1 -Version 1.0.0
```

결과는 `dist\ITDA-Setup-1.0.0\` 폴더에 생깁니다.

```text
ITDA-Setup-1.0.0.exe      # 설치 프로그램 (작음)
ITDA-Setup-1.0.0-1.bin    # 설치 데이터 조각 (각각 최대 약 2GB)
ITDA-Setup-1.0.0-2.bin
...
README.txt                # 사용자 안내
```

**이 폴더의 파일을 모두 함께 전달해야 합니다.** zip으로 보내려면 `-Zip`을 붙이면 `dist\ITDA-Setup-1.0.0.zip`도 만듭니다.

| 옵션 | 설명 |
| --- | --- |
| `-Version 1.0.0` | 설치 프로그램과 exe의 버전입니다. |
| `-Publisher "..."` | 제어판 "프로그램 제거" 목록에 나오는 게시자입니다. |
| `-ReleaseDir <경로>` | itda-release 위치를 지정합니다. 기본값은 `..\itda-release`입니다. |
| `-OllamaSetup <경로>` | 지정한 OllamaSetup.exe를 씁니다. 버전을 고정할 때 씁니다. 기본값은 `build\cache`에 있는 파일이고, 없으면 ollama.com에서 받습니다. |
| `-MinOllamaVersion 0.x.y` | 사용자 PC에 이보다 낮은 Ollama가 이미 깔려 있으면 런처가 업데이트를 안내합니다. |
| `-KeepModelFile` | 모델 등록 뒤에도 설치 폴더의 .gguf를 지우지 않습니다. 기본은 지웁니다. |
| `-NoModel` | .gguf 없이 빌드합니다. 이미 모델이 등록된 PC에서 앱만 업데이트할 때 씁니다. |
| `-Zip` | 결과 폴더를 zip으로도 묶습니다. 압축하지 않고 zip64로 묶으므로 5GB가 넘어도 됩니다. |

`build\cache`의 파일을 지우면 Python과 Ollama를 다시 받습니다. Ollama 버전을 올리려면 `build\cache\OllamaSetup.exe`를 지우고 다시 빌드합니다.

### build.ps1이 하는 일

1. `itda-release\backend`를 `build\staging\backend`로 복사합니다. `*.db`와 `__pycache__`는 빼고 복사합니다. itda-release에는 개발용 `itda.db`가 들어 있습니다.
2. Python 3.12.10 embeddable zip을 풀고 `python312._pth`를 고칩니다. `import site`를 켜고 `Lib\site-packages`와 `..\backend`를 경로에 추가합니다.
3. `py -3.12 -m pip install --no-index --find-links wheels\win_amd64 ... --target python\Lib\site-packages`로 패키지를 미리 넣습니다. 사용자 PC에서는 pip를 실행하지 않습니다.
4. 동봉한 python.exe로 import가 되는지 확인합니다.
5. `dotnet publish`로 `ITDA.exe`를 만듭니다. self-contained 단일 파일이고, 아이콘은 `assets\generated\itda.ico`입니다. 이어서 `launcher.json`을 생성합니다.
6. ISCC를 실행합니다. `model_name`과 경로는 `/D` 인자로 넘깁니다.

## 설치 결과 (사용자 PC)

| 위치 | 내용 |
| --- | --- |
| `%LOCALAPPDATA%\Programs\ITDA\` | `ITDA.exe`, `python\`, `backend\`, `model\`, `launcher.json`, `Binggrae.ttf` |
| `%LOCALAPPDATA%\ITDA\itda.db` | 기록 DB입니다. 재설치해도 유지되고, 제거할 때 지울지 묻습니다. |
| `%LOCALAPPDATA%\ITDA\logs\` | `launcher.log`, `server.log`, `ollama.log`(런처가 직접 띄운 경우), `ollama-create.log` |
| `%LOCALAPPDATA%\Programs\Ollama\` | Ollama입니다. 없을 때만 설치하고, 제거할 때도 남깁니다. |
| `%USERPROFILE%\.ollama\models\` | 등록된 모델입니다. 제거할 때도 남깁니다. |
| 바탕화면, 시작 메뉴 | "잇다" 바로가기 |

## 설계 메모

### 배포 형태: DiskSpanning

Inno Setup은 설치 데이터가 약 2,100,000,000바이트를 넘으면 DiskSpanning 없이 컴파일하지 않습니다. Windows도 4GB가 넘는 exe는 실행하지 못합니다. 그래서 모델을 함께 묶으면 exe 하나에 담을 수 없고, `exe + .bin 여러 개`로 나눠 한 폴더(zip)로 배포합니다.

- 조각 크기는 `DiskSliceSize=2000000000`으로 직접 고정했습니다(약 1.9GB씩 4개, 합계 약 6.7GB). Inno Setup 6.7에서 `max`로 두면 6.6GB짜리 .bin 하나가 나와서 FAT32 USB(파일당 4GB 한도)에 복사할 수 없기 때문입니다.
- zip 안에서 exe를 바로 실행하면 .bin을 찾지 못합니다. 이때 "디스크를 넣으세요" 창의 문구를 "압축을 먼저 풀어 주세요"로 바꿔 두었습니다(`[Messages] SelectDiskLabel2`).
- .gguf와 OllamaSetup.exe는 이미 압축된 데이터라 `nocompression`으로 넣습니다.
- 모델이 이미 Ollama에 등록된 PC에서 재설치하면 .gguf 복사를 건너뜁니다. 등록 여부는 이렇게 판단합니다.
  - Ollama가 실행 중이면 `/api/tags`에 묻습니다.
  - 꺼져 있으면 `~\.ollama\models\manifests\registry.ollama.ai\library\<model_name>\latest` 파일(또는 `OLLAMA_MODELS` 아래 같은 경로)로 봅니다.
  - 런처도 모델이 등록되어 있으면 `ollama create`를 건너뜁니다. 설치 폴더에 .gguf가 남아 있으면 설정에 따라 지웁니다.

### Ollama 설치: 인스톨러 `[Run]`

Ollama가 없을 때만 OllamaSetup.exe를 `{tmp}`에 풀어 `/VERYSILENT`로 설치합니다. Ollama도 사용자 권한으로 설치되므로 관리자 권한이 필요 없습니다.

- **장점:** 오래 걸리는 설치를 설치 마법사의 진행 표시 안에서 끝냅니다. 실패하면 설치 단계에서 바로 드러납니다. 설치 폴더에 약 1GB짜리 설치 파일을 남기지 않습니다.
- **단점:** 나중에 사용자가 Ollama를 지우면 런처는 "잇다를 다시 설치해 주세요"라고 안내만 합니다.

### 런처 동작 순서

1. Named Mutex(`Local\ITDA-Launcher-…`)로 중복 실행을 막습니다. 두 번째 실행은 첫 번째 런처에 신호만 보내고 끝납니다. 첫 번째 런처는 서버가 켜져 있으면 브라우저를 열고, 시작 중이면 진행 창을 앞으로 가져옵니다.
2. `/health`가 잇다 응답이면(`ok`, `model_name` 필드) 브라우저만 열고 끝냅니다. 포트 8000을 다른 프로그램이 쓰고 있으면 안내하고 끝냅니다. 오래 걸리는 모델 등록을 하기 전에 먼저 확인하려고, 서버를 띄우기 직전에도 한 번 더 확인합니다.
3. Ollama를 확인합니다. `ollama.exe`는 `%LOCALAPPDATA%\Programs\Ollama`와 레지스트리의 최신 PATH에서 찾습니다. 11434 포트가 응답하지 않으면 `ollama serve`를 띄웁니다. 방금 설치된 Ollama 트레이 앱이 켜지는 중일 수 있어서, Ollama 프로세스가 보이면 10초 기다린 뒤에 띄웁니다.
4. `/api/tags`에 모델이 없으면(`ollama list`와 같은 정보) `model\`에서 `ollama create <model_name> -f Modelfile`을 실행합니다. 진행률은 진행 창에 표시합니다. 등록이 끝나면 설정에 따라 .gguf를 지웁니다. 모델도 .gguf도 없으면 재설치를 안내합니다.
5. `python\python.exe -m app`을 작업 폴더 `backend\`에서 실행합니다. 이때 `ITDA_DB`와 `OLLAMA_HOST`를 넘깁니다. `/health`가 응답할 때까지 기다린 뒤 브라우저를 엽니다.

- 자식 프로세스는 `CreateNoWindow`로 띄우고 Job Object(`KILL_ON_JOB_CLOSE`)에 넣습니다. 트레이에서 종료하거나 런처가 비정상 종료되면 서버도 함께 끝납니다. `ollama serve`는 런처가 직접 띄운 경우에만 Job에 들어가므로, 사용자가 원래 쓰던 Ollama는 건드리지 않습니다.
- 서버가 도중에 멈추면 안내한 뒤 런처도 끝냅니다.

### 런처 설정 (`launcher.json`)

설치 폴더의 `launcher.json`은 build.ps1이 만듭니다. `%LOCALAPPDATA%\ITDA\launcher.json`이 있으면 그 값이 우선합니다. 이 파일은 재설치해도 유지됩니다.

```json
{
  "DeleteModelFileAfterRegister": true,
  "MinOllamaVersion": "",
  "OllamaStartTimeoutSeconds": 60,
  "ServerStartTimeoutSeconds": 120
}
```

### 알아 둘 점

- **코드 서명:** 서명하지 않으므로 SmartScreen 경고가 뜹니다. 사용자는 [추가 정보] → [실행]을 눌러야 합니다.
- **`allow_lan: true`:** 이렇게 빌드하면 서버가 0.0.0.0에 바인딩되어, 첫 실행 때 Windows 방화벽 허용 창이 뜹니다.
- **Ollama 버전:** Modelfile이 `RENDERER gemma4` 같은 최신 지시어를 쓰므로 오래된 Ollama에서는 `ollama create`가 실패할 수 있습니다. 확인된 최소 버전을 `-MinOllamaVersion`으로 넣어 두는 것을 권장합니다.
- **디스크:** 모델 등록 중에는 설치 폴더와 Ollama 폴더에 모델이 잠시 두 벌 있어 약 15GB가 필요합니다. 런처는 등록 전에 빈 공간을 확인합니다.
- **폰트:** 런처 창은 `Binggrae.ttf`를 PrivateFontCollection으로 씁니다. 설치 마법사 폰트는 바꾸지 않았습니다.

## 에셋 다시 만들기

`assets/logo.png`나 `assets/mascot.png`를 바꿨을 때만 실행합니다.

```bash
pip install pillow
python tools/make_assets.py   # assets/generated/ 갱신 → 커밋
```

아이콘에는 로고에서 글자를 뺀 심볼만 씁니다. 16px에서는 글자가 뭉개지기 때문입니다. 마법사 큰 이미지는 마스코트를, 작은 이미지는 심볼을 씁니다.

## 런처만 개발할 때

```powershell
dotnet build launcher -c Debug
```

런처는 자기 exe가 있는 폴더를 설치 폴더로 봅니다. 그래서 디버그 실행 전에 `build\staging` 내용(`python\`, `backend\`, `model\`)을 출력 폴더에 복사하거나, 한 번 빌드한 뒤 staging에서 `ITDA.exe`를 실행해 확인합니다. Linux에서는 `dotnet build -p:EnableWindowsTargeting=true`로 컴파일 확인만 할 수 있습니다.

테스트 항목은 [TESTING.md](TESTING.md)를 봅니다.
