using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DragonDeskPet.AI;

public sealed class OpenAiCompatibleProvider : IAiProvider
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly Regex AuthorizationPattern = new(
        @"(?i)(authorization\s*[:=]\s*bearer\s+)[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DataUriPattern = new(
        @"data:image/[^;\s]+;base64,[A-Za-z0-9+/=]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly AiProviderDescriptor _descriptor;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly HttpClient _client;

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
        _client = SharedClient;
    }

    internal OpenAiCompatibleProvider(
        AiProviderDescriptor descriptor,
        string baseUrl,
        string model,
        string apiKey,
        HttpClient client)
        : this(descriptor, baseUrl, model, apiKey)
    {
        _client = client;
    }

    public string DisplayName => _descriptor.DisplayName;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_baseUrl)
        && !string.IsNullOrWhiteSpace(_model)
        && (!_descriptor.RequiresApiKey || !string.IsNullOrWhiteSpace(_apiKey));

    public async Task<string> SendAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return $"{DisplayName} 还没有配置完整。请检查 Base URL、模型名和 API Key。";
        }

        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("问题内容不能为空。", nameof(request));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(_baseUrl));
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        httpRequest.Content = new StringContent(
            SerializeRequest(_model, request),
            Encoding.UTF8,
            "application/json");

        using var response = await _client.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (request.Image is not null
                && response.StatusCode is HttpStatusCode.BadRequest
                    or HttpStatusCode.UnsupportedMediaType
                    or HttpStatusCode.UnprocessableEntity)
            {
                throw new InvalidOperationException("当前模型或接口可能不支持图片输入，请更换支持视觉的模型后重试。");
            }

            throw new InvalidOperationException(
                $"{DisplayName} 返回 {(int)response.StatusCode}：{ExtractError(body, _apiKey)}");
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

    internal static string SerializeRequest(string model, AiRequest request)
    {
        object userContent = request.Image is null
            ? request.Prompt
            : new object[]
            {
                new { type = "text", text = request.Prompt },
                new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = $"data:{request.Image.MimeType};base64,{Convert.ToBase64String(request.Image.Data.Span)}"
                    }
                }
            };

        return JsonSerializer.Serialize(new
        {
            model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You are the careful AI brain of a small white-and-lilac dragon-girl desktop pet. Be concise, accurate, warm, and never sacrifice correctness for roleplay."
                },
                new { role = "user", content = userContent }
            }
        });
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

    private static string ExtractError(string body, string apiKey)
    {
        string errorText;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message))
                {
                    errorText = message.GetString() ?? "未知错误";
                }
                else
                {
                    errorText = error.ToString();
                }

                return SanitizeError(errorText, apiKey);
            }
        }
        catch (JsonException)
        {
            // Fall back to a safely truncated raw response.
        }

        return SanitizeError(body, apiKey);
    }

    private static string SanitizeError(string value, string apiKey)
    {
        var sanitized = DataUriPattern.Replace(value, "[图片数据已隐藏]");
        sanitized = AuthorizationPattern.Replace(sanitized, "$1[已隐藏]");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            sanitized = sanitized.Replace(apiKey, "[已隐藏]", StringComparison.Ordinal);
        }

        sanitized = sanitized.ReplaceLineEndings(" ").Trim();
        return sanitized.Length > 240 ? sanitized[..240] + "…" : sanitized;
    }
}
