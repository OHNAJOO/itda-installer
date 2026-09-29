namespace Itda.Launcher;

/// <summary>설치 폴더(읽기 전용 취급)와 사용자 데이터 폴더(%LOCALAPPDATA%\ITDA, 재설치해도 유지)의 경로.</summary>
internal static class AppPaths
{
    public const string ServerUrl = "http://127.0.0.1:8000";
    public const string OllamaUrl = "http://127.0.0.1:11434";
    public const int ServerPort = 8000;
    public const int OllamaPort = 11434;

    /// <summary>ITDA.exe가 있는 폴더. 단일 파일 exe여도 exe 위치를 가리킨다.</summary>
    public static string AppDir { get; } = AppContext.BaseDirectory.TrimEnd('\\');

    public static string PythonExe => Path.Combine(AppDir, "python", "python.exe");
    public static string BackendDir => Path.Combine(AppDir, "backend");
    public static string SettingsYaml => Path.Combine(BackendDir, "config", "settings.yaml");
    public static string ModelDir => Path.Combine(AppDir, "model");
    public static string Modelfile => Path.Combine(ModelDir, "Modelfile");
    public static string FontFile => Path.Combine(AppDir, "Binggrae.ttf");
    public static string AppConfigFile => Path.Combine(AppDir, "launcher.json");

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ITDA");

    public static string LogsDir => Path.Combine(DataDir, "logs");
    public static string DbPath => Path.Combine(DataDir, "itda.db");
    public static string UserConfigFile => Path.Combine(DataDir, "launcher.json");
}
