namespace KiwiMind.Application.Common.Settings;

public class GroqSettings
{
    public const string SectionName = "Groq";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "llama-3.3-70b-versatile";
}
