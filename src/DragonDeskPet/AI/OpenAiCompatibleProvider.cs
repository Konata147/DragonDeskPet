using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;

namespace DragonDeskPet.AI;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly AiProviderDescriptor _descriptor;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;

    public OpenAiCompatibleProvider(
        AiProviderDescriptor descriptor,
        string baseUrl,
        string model,
        string apiKey)
    {
        _descriptor = descriptor;
        _baseUrl = baseUrl;
        _model = model;
        _apiKey = apiKey;
    }

    public string DisplayName => _descriptor.DisplayName;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_baseUrl)
        && !string.IsNullOrWhiteSpace(_model)
        && (!_descriptor.RequiresApiKey || !string.IsNullOrWhiteSpace(_apiKey));

    public async Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return $"{DisplayName} 还没有配置完整。请检查 Base URL、模型名和 API Key。";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(_baseUrl));
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        request.Content = JsonContent.Create(new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are the careful AI brain of a small white-and-lilac dragon-girl desktop pet. Be concise, accurate, warm, and never sacrifice correctness for roleplay."
                },
                new { role = "user", content = prompt }
            }
        });

        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{DisplayName} 返回 {(int)response.StatusCode}：{ExtractError(body)}");
        }

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString()?.Trim() ?? "我收到了响应，但里面没有可显示的文字。";
        }

        return "提供商返回了暂不支持的响应格式。";
    }

    private static Uri BuildEndpoint(string baseUrl)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (!trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/chat/completions";
        }

        return new Uri(trimmed, UriKind.Absolute);
    }

    private static string ExtractError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? "未知错误";
                }

                return error.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall back to a safely truncated raw response.
        }

        return body.Length > 240 ? body[..240] + "…" : body;
    }
}
