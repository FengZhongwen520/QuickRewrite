using System.Text.Json.Serialization;

namespace QuickRewrite.Models;

public enum AiProviderKind
{
    Demo,
    OpenAiCompatible
}

public sealed class AppSettings
{
    public string Hotkey { get; set; } = "Ctrl+Alt+R";
    public AiProviderKind Provider { get; set; } = AiProviderKind.Demo;
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxInputCharacters { get; set; } = 5000;

    public string ApiKeyProtected { get; set; } = "";

    [JsonIgnore]
    public string ApiKey { get; set; } = "";

    public AppSettings Clone()
    {
        return new AppSettings
        {
            Hotkey = Hotkey,
            Provider = Provider,
            BaseUrl = BaseUrl,
            Model = Model,
            TimeoutSeconds = TimeoutSeconds,
            MaxInputCharacters = MaxInputCharacters,
            ApiKeyProtected = ApiKeyProtected,
            ApiKey = ApiKey
        };
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            Hotkey = "Ctrl+Alt+R";
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            Model = "gpt-4o-mini";
        }

        TimeoutSeconds = Math.Clamp(TimeoutSeconds <= 0 ? 30 : TimeoutSeconds, 5, 120);
        MaxInputCharacters = Math.Clamp(MaxInputCharacters <= 0 ? 5000 : MaxInputCharacters, 100, 50_000);
    }
}
