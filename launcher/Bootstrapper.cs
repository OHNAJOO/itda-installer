namespace Itda.Launcher;

/// <summary>진행 창에 보여 줄 상태. Percent가 null이면 막대가 계속 흐른다.</summary>
internal sealed record StartupStatus(string Title, string Detail = "", int? Percent = null);

internal enum StartupResult
{
    /// <summary>이 런처가 서버를 띄웠다. 트레이에 남아 서버를 관리한다.</summary>
    Started,

    /// <summary>이미 다른 곳에서 잇다 서버가 켜져 있다. 브라우저만 열고 끝낸다.</summary>
    AlreadyRunning,
}

/// <summary>
/// 런처 동작 순서 (Linux run.sh·setup.sh를 옮긴 것).
/// 1. /health가 잇다면 브라우저만 연다 / 2. Ollama 확인·기동 / 3. 모델 확인·등록
/// 4. 포트 8000 확인 / 5. 서버 기동 → /health 대기.
/// </summary>
internal sealed class Bootstrapper(LauncherConfig config, Ollama ollama, Server server)
{
    public async Task<StartupResult> RunAsync(IProgress<StartupStatus> progress, CancellationToken ct)
    {
        // 1. 이미 켜져 있으면 끝. 다른 프로그램이 8000을 쓰면 오래 걸리는 단계 전에 먼저 알린다.
        progress.Report(new StartupStatus("잇다를 시작하고 있어요"));
        switch (await server.CheckPortAsync(ct))
        {
            case PortState.Itda:
                Log.Info("잇다 서버가 이미 응답합니다. 브라우저만 엽니다.");
                return StartupResult.AlreadyRunning;
            case PortState.Other:
                throw PortInUse();
        }

        if (!File.Exists(AppPaths.PythonExe) || !File.Exists(AppPaths.SettingsYaml))
            throw new UserFacingException("잇다 프로그램 파일 일부가 없습니다. 잇다를 다시 설치해 주세요.");
        var settings = ItdaSettings.Load();
        Log.Info($"모델 이름: {settings.ModelName}, allow_lan: {settings.AllowLan}");

        // 2. Ollama
        progress.Report(new StartupStatus("AI 엔진(Ollama)을 확인하고 있어요"));
        var exe = Ollama.FindExe();
        if (exe is null && await ollama.GetVersionAsync(ct) is null)
            throw new UserFacingException(
                "AI 엔진(Ollama)이 설치되어 있지 않습니다.\n\n잇다 설치 프로그램을 다시 실행하면 함께 설치됩니다.");
        Log.Info($"ollama.exe: {exe ?? "(찾지 못함, 이미 실행 중인 Ollama 사용)"}");
        if (exe is not null)
            await ollama.EnsureRunningAsync(exe, config.OllamaStartTimeoutSeconds, ct);

        var version = await ollama.GetVersionAsync(ct) ?? "";
        Log.Info($"Ollama 버전: {version}");
        if (config.MinOllamaVersion.Length > 0 && Ollama.IsOlderThan(version, config.MinOllamaVersion))
            throw new UserFacingException(
                $"설치된 AI 엔진(Ollama {version})이 오래되어 잇다 모델을 쓸 수 없습니다.\n\n" +
                $"https://ollama.com/download 에서 최신 버전(최소 {config.MinOllamaVersion})으로 업데이트한 뒤 다시 실행해 주세요.");

        // 3. 모델
        progress.Report(new StartupStatus("AI 모델을 확인하고 있어요"));
        if (!await ollama.HasModelAsync(settings.ModelName, ct))
        {
            await RegisterModelAsync(exe, settings, progress, ct);
        }
        else
        {
            // 이미 등록됨: ollama create를 건너뛴다. 인스톨러가 등록 여부를 놓쳐 .gguf를 복사했다면 여기서 정리한다.
            Log.Info("모델이 이미 등록되어 있어 등록을 건너뜁니다.");
            DeleteModelFileIfConfigured(settings);
        }

        if (settings.SummaryModel != settings.ModelName && !await ollama.HasModelAsync(settings.SummaryModel, ct))
            Log.Info($"주의: 요약 모델 '{settings.SummaryModel}'이 없습니다. 요약은 템플릿 문장으로 나옵니다.");

        // 4. 포트 (모델 등록 동안 다른 프로그램이 잡았을 수 있어 한 번 더)
        switch (await server.CheckPortAsync(ct))
        {
            case PortState.Itda:
                return StartupResult.AlreadyRunning;
            case PortState.Other:
                throw PortInUse();
        }

        // 5. 서버
        progress.Report(new StartupStatus("잇다 화면을 준비하고 있어요", "처음에는 조금 더 걸릴 수 있어요."));
        server.Start();
        await server.WaitHealthyAsync(config.ServerStartTimeoutSeconds, ct);
        Log.Info("잇다 서버 준비 완료");
        return StartupResult.Started;
    }

    private async Task RegisterModelAsync(string? exe, ItdaSettings settings, IProgress<StartupStatus> progress,
        CancellationToken ct)
    {
        if (!File.Exists(settings.GgufPath))
            throw new UserFacingException(
                "AI 모델 파일이 필요합니다.\n\n" +
                "모델이 AI 엔진(Ollama)에 등록되어 있지 않고, 설치 폴더에 모델 파일도 없습니다.\n" +
                "잇다 설치 프로그램을 다시 실행해 주세요.");
        if (exe is null)
            throw new UserFacingException("AI 엔진(Ollama) 실행 파일을 찾을 수 없습니다. 잇다를 다시 설치해 주세요.");

        EnsureDiskSpace(settings.GgufPath);

        const string title = "AI 모델을 처음 등록하고 있어요";
        const string hint = "처음 한 번만 필요해요. 몇 분 걸릴 수 있어요.";
        Log.Info($"모델 등록 시작: {settings.ModelName}");
        progress.Report(new StartupStatus(title, hint));
        await ollama.CreateModelAsync(exe, settings.ModelName,
            (line, pct) => progress.Report(new StartupStatus(title, pct is null ? hint : $"{hint}  ({pct}%)", pct)), ct);

        if (!await ollama.HasModelAsync(settings.ModelName, ct))
            throw new UserFacingException("AI 모델 등록이 끝났지만 목록에서 찾을 수 없습니다. 잇다를 다시 실행해 주세요.");
        Log.Info("모델 등록 완료");
        DeleteModelFileIfConfigured(settings);
    }

    /// <summary>모델이 Ollama에 등록된 뒤에는 설치 폴더의 .gguf가 필요 없다 (Ollama가 사본을 가짐).</summary>
    private void DeleteModelFileIfConfigured(ItdaSettings settings)
    {
        if (!config.DeleteModelFileAfterRegister || !File.Exists(settings.GgufPath)) return;
        try
        {
            File.Delete(settings.GgufPath);
            Log.Info($"설치 폴더의 모델 파일을 지웠습니다: {settings.GgufPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("모델 파일을 지우지 못했습니다 (다음 실행에 영향 없음)", ex);
        }
    }

    /// <summary>Ollama는 등록할 때 모델을 자기 폴더로 복사하므로 그만큼 빈 공간이 필요하다.</summary>
    private static void EnsureDiskSpace(string ggufPath)
    {
        try
        {
            var modelsDir = Ollama.ModelsDir();
            var root = Path.GetPathRoot(Path.GetFullPath(modelsDir));
            if (root is null) return;
            var free = new DriveInfo(root).AvailableFreeSpace;
            var need = new FileInfo(ggufPath).Length + 1L * 1024 * 1024 * 1024;
            Log.Info($"모델 폴더 {modelsDir}: 남은 공간 {free / 1e9:F1}GB, 필요 {need / 1e9:F1}GB");
            if (free < need)
                throw new UserFacingException(
                    $"디스크 공간이 부족합니다.\n\n{root} 드라이브에 {need / 1e9:F0}GB 이상 빈 공간이 필요합니다 " +
                    $"(현재 {free / 1e9:F1}GB).\n공간을 확보한 뒤 다시 실행해 주세요.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error("디스크 공간을 확인하지 못했습니다 (계속 진행)", ex);
        }
    }

    private static UserFacingException PortInUse() => new(
        "다른 프로그램이 포트 8000을 쓰고 있어 잇다를 켤 수 없습니다.\n\n" +
        "열려 있는 다른 프로그램(개발용 서버 등)을 끄거나 컴퓨터를 다시 시작한 뒤 다시 실행해 주세요.");
}
