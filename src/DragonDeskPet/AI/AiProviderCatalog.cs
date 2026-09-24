namespace DragonDeskPet.AI;

public static class AiProviderCatalog
{
    public static IReadOnlyList<AiProviderDescriptor> All { get; } =
    [
        new("Offline", "离线 / 尚未配置", "", "", false, false),
        new("OpenAI", "OpenAI", "https://api.openai.com/v1", "gpt-5-mini", true, true),
        new("Gemini", "Google Gemini", "https://generativelanguage.googleapis.com", "gemini-2.5-flash", false, true),
        new("Anthropic", "Anthropic Claude", "https://api.anthropic.com", "claude-sonnet-4-5", false, true),
        new("DeepSeek", "DeepSeek", "https://api.deepseek.com", "deepseek-flash", true, true),
        new("OpenRouter", "OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4.1-mini", true, true),
        new("Ollama", "Ollama（本地）", "http://localhost:11434/v1", "qwen3:8b", true, false),
        new("LMStudio", "LM Studio（本地）", "http://localhost:1234/v1", "local-model", true, false),
        new("Custom", "自定义 OpenAI-compatible API", "", "", true, false)
    ];

    public static AiProviderDescriptor Find(string id) =>
        All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}
