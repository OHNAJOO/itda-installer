using System.Text;

namespace Itda.Launcher;

/// <summary>%LOCALAPPDATA%\ITDA\logs 아래 파일에 한 줄씩 남긴다. 5MB를 넘으면 .old로 한 번 돌린다.</summary>
internal sealed class LogFile
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly object _lock = new();

    public string Path { get; }

    public LogFile(string name)
    {
        Directory.CreateDirectory(AppPaths.LogsDir);
        Path = System.IO.Path.Combine(AppPaths.LogsDir, name);
        try
        {
            var info = new FileInfo(Path);
            if (info.Exists && info.Length > MaxBytes)
                File.Move(Path, Path + ".old", overwrite: true);
        }
        catch (IOException) { }
    }

    public void Write(string line)
    {
        lock (_lock)
        {
            try
            {
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>파일 끝의 몇 줄. 오류 안내에 붙인다.</summary>
    public string Tail(int lines)
    {
        lock (_lock)
        {
            try
            {
                using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                var all = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                return string.Join("\n", all.TakeLast(lines).Select(l => l.TrimEnd('\r')));
            }
            catch (IOException) { return ""; }
        }
    }
}

internal static class Log
{
    private static readonly Lazy<LogFile> Main = new(() => new LogFile("launcher.log"));

    public static string FilePath => Main.Value.Path;

    public static void Info(string message) => Main.Value.Write("[INFO] " + message);

    public static void Error(string message, Exception? ex = null) =>
        Main.Value.Write("[ERROR] " + message + (ex is null ? "" : Environment.NewLine + ex));
}
