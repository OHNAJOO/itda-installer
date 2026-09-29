using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Itda.Launcher;

/// <summary>Ollama 찾기·기동·모델 확인·모델 등록.</summary>
internal sealed partial class Ollama(HttpClient http, JobObject job)
{
    private readonly LogFile _serveLog = new("ollama.log");

    /// <summary>런처가 직접 띄운 ollama serve. 이 경우에만 런처 종료 때 함께 끝난다(Job Object).</summary>
    public Process? OwnedServe { get; private set; }

    /// <summary>
    /// ollama.exe 전체 경로. 방금 설치된 경우 PATH가 이 프로세스에 반영되지 않으므로
    /// 기본 설치 위치와 레지스트리의 최신 PATH까지 직접 찾는다.
    /// </summary>
    public static string? FindExe()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
        };
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Registry.CurrentUser.OpenSubKey("Environment")?.GetValue("Path") as string,
            Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")?.GetValue("Path") as string,
        };
        foreach (var dir in paths.Where(p => p is not null).SelectMany(p => p!.Split(';', StringSplitOptions.RemoveEmptyEntries)))
        {
            try { candidates.Add(Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"')), "ollama.exe")); }
            catch (ArgumentException) { }
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>11434 포트가 Ollama로 응답하면 버전 문자열, 아니면 null.</summary>
    public async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        try
        {
            var r = await http.GetFromJsonAsync<VersionResponse>($"{AppPaths.OllamaUrl}/api/version", ct);
            return r?.Version ?? "";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            return null;
        }
    }

    /// <summary>
    /// Ollama가 응답하지 않으면 ollama serve를 띄우고 응답할 때까지 기다린다.
    /// Ollama 트레이 앱이 막 켜지는 중일 수 있어, 먼저 잠시 기다린 뒤에도 응답이 없을 때만 띄운다(중복 serve 방지).
    /// </summary>
    public async Task EnsureRunningAsync(string exe, int timeoutSeconds, CancellationToken ct)
    {
        if (await GetVersionAsync(ct) is not null) return;

        if (Process.GetProcessesByName("ollama app").Length > 0 || Process.GetProcessesByName("ollama").Length > 0)
        {
            Log.Info("Ollama 프로세스가 있으나 응답이 없어 잠시 기다립니다.");
            if (await WaitAsync(TimeSpan.FromSeconds(10), ct)) return;
        }

        Log.Info("ollama serve를 직접 띄웁니다.");
        OwnedServe = ChildProcess.Start(exe, "serve", Path.GetDirectoryName(exe)!, _serveLog,
            new Dictionary<string, string> { ["OLLAMA_HOST"] = $"127.0.0.1:{AppPaths.OllamaPort}" }, job: job);

        if (await WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct))
        {
            if (OwnedServe.HasExited)
            {
                // 그 사이 다른 Ollama(트레이 앱)가 포트를 잡아 우리 serve가 끝난 경우: 남의 것이므로 소유하지 않는다
                Log.Info("직접 띄운 serve는 끝났지만 다른 Ollama가 응답합니다.");
                OwnedServe = null;
            }
            return;
        }
        throw new UserFacingException(
            $"AI 엔진(Ollama)이 {timeoutSeconds}초 안에 켜지지 않았습니다.\n\n" +
            "컴퓨터를 다시 시작한 뒤 잇다를 다시 실행해 주세요.\n" +
            $"계속 안 되면 이 파일을 담당자에게 보내 주세요:\n{_serveLog.Path}");
    }

    private async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (await GetVersionAsync(ct) is not null) return true;
            await Task.Delay(500, ct);
        }
        return false;
    }

    /// <summary>등록된 모델 이름 목록 (`ollama list`와 같은 정보를 HTTP API로 받음).</summary>
    public async Task<bool> HasModelAsync(string name, CancellationToken ct)
    {
        var r = await http.GetFromJsonAsync<TagsResponse>($"{AppPaths.OllamaUrl}/api/tags", ct);
        var names = (r?.Models ?? []).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.Contains(name) || names.Contains(name + ":latest");
    }

    /// <summary>
    /// model/ 폴더에서 `ollama create name -f Modelfile`. 진행률(%)이 나오면 onProgress로 알린다.
    /// </summary>
    public async Task CreateModelAsync(string exe, string name, Action<string, int?> onProgress, CancellationToken ct)
    {
        var log = new LogFile("ollama-create.log");
        using var process = ChildProcess.Start(exe, $"create \"{name}\" -f Modelfile", AppPaths.ModelDir, log,
            new Dictionary<string, string> { ["OLLAMA_HOST"] = $"127.0.0.1:{AppPaths.OllamaPort}" },
            onLine: line =>
            {
                var pct = PercentPattern().Match(line);
                onProgress(line, pct.Success ? int.Parse(pct.Groups[1].Value) : null);
            },
            job: job);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        // 비동기 출력 읽기가 끝날 때까지 한 번 더 기다린다
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new UserFacingException(
                "AI 모델을 등록하지 못했습니다.\n\n" +
                "디스크 공간이 충분한지 확인한 뒤 잇다를 다시 실행해 주세요.\n" +
                "Ollama가 오래된 버전이면 업데이트가 필요할 수 있습니다.\n\n" +
                $"마지막 메시지:\n{log.Tail(3)}\n\n로그: {log.Path}");
    }

    /// <summary>모델이 저장되는 폴더 (OLLAMA_MODELS가 있으면 그 값). 디스크 공간 확인용.</summary>
    public static string ModelsDir()
    {
        var custom = Environment.GetEnvironmentVariable("OLLAMA_MODELS")
            ?? Registry.CurrentUser.OpenSubKey("Environment")?.GetValue("OLLAMA_MODELS") as string;
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models")
            : custom;
    }

    public static bool IsOlderThan(string version, string minimum)
    {
        static Version? Parse(string v)
        {
            var m = Regex.Match(v, @"\d+(\.\d+){1,3}");
            return m.Success && Version.TryParse(m.Value, out var parsed) ? parsed : null;
        }
        var have = Parse(version);
        var need = Parse(minimum);
        return have is not null && need is not null && have < need;
    }

    [GeneratedRegex(@"(\d{1,3})%")]
    private static partial Regex PercentPattern();

    private sealed record VersionResponse([property: JsonPropertyName("version")] string? Version);

    private sealed record TagsResponse([property: JsonPropertyName("models")] List<TagModel>? Models);

    private sealed record TagModel([property: JsonPropertyName("name")] string Name);
}
