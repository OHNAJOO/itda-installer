using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Itda.Launcher;

/// <summary>콘솔 창 없이 자식 프로세스를 띄우고 출력(stdout·stderr)을 로그 파일로 보낸다.</summary>
internal static partial class ChildProcess
{
    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();

    public static Process Start(string exe, string arguments, string workingDir, LogFile log,
        IReadOnlyDictionary<string, string>? env = null, Action<string>? onLine = null, JobObject? job = null)
    {
        var psi = new ProcessStartInfo(exe, arguments)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (env is not null)
            foreach (var (k, v) in env) psi.Environment[k] = v;

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        DataReceivedEventHandler handler = (_, e) =>
        {
            if (e.Data is null) return;
            var line = AnsiEscape().Replace(e.Data, "").Trim();
            if (line.Length == 0) return;
            log.Write(line);
            onLine?.Invoke(line);
        };
        process.OutputDataReceived += handler;
        process.ErrorDataReceived += handler;

        log.Write($"--- 시작: \"{exe}\" {arguments} (작업 폴더 {workingDir})");
        process.Start();
        // 자식이 손자 프로세스를 만들기 전에 넣는다 (서버·ollama 모두 시작 직후엔 자식을 만들지 않음)
        job?.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
