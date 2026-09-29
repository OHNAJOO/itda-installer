using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Itda.Launcher;

internal enum PortState
{
    Free,
    Itda,
    Other,
}

/// <summary>잇다 서버(python -m app) 확인·기동.</summary>
internal sealed class Server(HttpClient http, JobObject job)
{
    public LogFile Log { get; } = new("server.log");

    public Process? Process { get; private set; }

    /// <summary>
    /// 포트 8000 상태. /health가 잇다 응답(ok, model_name 필드)이면 Itda,
    /// 응답은 없는데 포트가 잡혀 있거나 다른 응답이면 Other.
    /// </summary>
    public async Task<PortState> CheckPortAsync(CancellationToken ct)
    {
        if (await IsItdaHealthyAsync(ct)) return PortState.Itda;
        return IsPortBound() ? PortState.Other : PortState.Free;
    }

    public async Task<bool> IsItdaHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync($"{AppPaths.ServerUrl}/health", ct);
            if (!r.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True
                && root.TryGetProperty("model_name", out _);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            return false;
        }
    }

    /// <summary>127.0.0.1:8000과 0.0.0.0:8000 중 하나라도 이미 쓰이고 있으면 true.</summary>
    private static bool IsPortBound()
    {
        foreach (var address in new[] { IPAddress.Loopback, IPAddress.Any })
        {
            try
            {
                var listener = new TcpListener(address, AppPaths.ServerPort) { ExclusiveAddressUse = true };
                listener.Start();
                listener.Stop();
            }
            catch (SocketException)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>작업 폴더 backend\ 에서 python -m app. DB는 사용자 데이터 폴더로 보낸다.</summary>
    public void Start()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var env = new Dictionary<string, string>
        {
            ["ITDA_DB"] = AppPaths.DbPath,
            ["OLLAMA_HOST"] = AppPaths.OllamaUrl,
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
            ["PYTHONUNBUFFERED"] = "1",
        };
        Process = ChildProcess.Start(AppPaths.PythonExe, "-m app", AppPaths.BackendDir, Log, env, job: job);
    }

    public async Task WaitHealthyAsync(int timeoutSeconds, CancellationToken ct)
    {
        var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < until)
        {
            if (Process is { HasExited: true })
                throw new UserFacingException(
                    "잇다 서버가 시작 중에 멈췄습니다.\n\n" +
                    $"마지막 메시지:\n{Log.Tail(5)}\n\n로그: {Log.Path}");
            if (await IsItdaHealthyAsync(ct)) return;
            await Task.Delay(500, ct);
        }
        throw new UserFacingException(
            $"잇다 서버가 {timeoutSeconds}초 안에 준비되지 않았습니다.\n\n" +
            $"컴퓨터를 다시 시작한 뒤 다시 실행해 주세요.\n로그: {Log.Path}");
    }
}
