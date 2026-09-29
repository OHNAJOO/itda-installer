using System.Text.Json;
using System.Text.RegularExpressions;

namespace Itda.Launcher;

/// <summary>
/// 런처 설정. 설치 폴더의 launcher.json(build.ps1이 만듦)을 읽고,
/// %LOCALAPPDATA%\ITDA\launcher.json이 있으면 그 값으로 덮어쓴다(재설치해도 유지되는 사용자 설정).
/// </summary>
internal sealed class LauncherConfig
{
    /// <summary>모델 등록이 끝나면 설치 폴더의 .gguf를 지운다. Ollama가 사본을 가지므로 약 5GB를 아낀다.</summary>
    public bool DeleteModelFileAfterRegister { get; set; } = true;

    /// <summary>이 버전보다 낮은 Ollama면 업데이트를 안내한다. 비우면 검사하지 않는다. 예: "0.12.0"</summary>
    public string MinOllamaVersion { get; set; } = "";

    public int OllamaStartTimeoutSeconds { get; set; } = 60;
    public int ServerStartTimeoutSeconds { get; set; } = 120;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LauncherConfig Load()
    {
        var config = new LauncherConfig();
        foreach (var path in new[] { AppPaths.AppConfigFile, AppPaths.UserConfigFile })
        {
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                // 파일에 적힌 키만 덮어쓴다
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var target = typeof(LauncherConfig).GetProperty(prop.Name,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.IgnoreCase);
                    if (target is null) continue;
                    target.SetValue(config, prop.Value.Deserialize(target.PropertyType, Options));
                }
                Log.Info($"설정 읽음: {path}");
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
                Log.Error($"설정 파일을 읽지 못해 무시합니다: {path}", ex);
            }
        }
        return config;
    }
}

/// <summary>backend/config/settings.yaml과 model/Modelfile에서 런처가 필요한 값만 읽는다.</summary>
internal sealed record ItdaSettings(string ModelName, string SummaryModel, bool AllowLan, string GgufFileName)
{
    public string GgufPath => Path.Combine(AppPaths.ModelDir, GgufFileName);

    public static ItdaSettings Load()
    {
        var yaml = File.ReadAllText(AppPaths.SettingsYaml);
        var modelName = YamlScalar(yaml, "model_name")
            ?? throw new UserFacingException("설정 파일에서 model_name을 찾을 수 없습니다. 잇다를 다시 설치해 주세요.");
        var summaryModel = YamlScalar(yaml, "summary_model") ?? modelName;
        var allowLan = string.Equals(YamlScalar(yaml, "allow_lan"), "true", StringComparison.OrdinalIgnoreCase);

        // Modelfile은 "FROM ./<gguf>" 상대경로 (setup.sh와 같은 방식으로 읽음)
        var gguf = File.ReadLines(AppPaths.Modelfile)
            .Select(l => Regex.Match(l, @"^FROM\s+\./(\S+)\s*$"))
            .FirstOrDefault(m => m.Success)?.Groups[1].Value
            ?? throw new UserFacingException("Modelfile에서 모델 파일 이름을 찾을 수 없습니다. 잇다를 다시 설치해 주세요.");

        return new ItdaSettings(modelName, summaryModel, allowLan, gguf);
    }

    /// <summary>한 줄짜리 "key: value  # 주석" 형태만 읽는다. 따옴표는 벗긴다.</summary>
    private static string? YamlScalar(string yaml, string key)
    {
        var m = Regex.Match(yaml, $@"^{Regex.Escape(key)}:[ \t]*(""[^""]*""|'[^']*'|[^#\r\n]*)", RegexOptions.Multiline);
        if (!m.Success) return null;
        var value = m.Groups[1].Value.Trim().Trim('"', '\'');
        return value.Length == 0 ? null : value;
    }
}

/// <summary>사용자에게 그대로 보여 줄 한국어 안내를 담은 예외.</summary>
internal sealed class UserFacingException(string message) : Exception(message);
