using DragonDeskPet.Services;

namespace DragonDeskPet.AI;

public sealed class AiProviderFactory
{
    public IAiProvider Create(AppSettings settings)
    {
        var descriptor = AiProviderCatalog.Find(settings.Provider);
        if (descriptor.Id == "Offline")
        {
            return new UnavailableAiProvider("离线模式", "先在设置里选择 AI 提供商、模型并填写所需凭据吧。");
        }

        if (!descriptor.UsesOpenAiCompatibleTransport)
        {
            return new UnavailableAiProvider(
                descriptor.DisplayName,
                $"{descriptor.DisplayName} 已纳入 Provider 架构，原生协议会在后续版本接入；V0.1 可先使用 OpenAI-compatible 服务。");
        }

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? descriptor.DefaultBaseUrl : settings.BaseUrl.Trim();
        var model = string.IsNullOrWhiteSpace(settings.Model) ? descriptor.DefaultModel : settings.Model.Trim();
        return new OpenAiCompatibleProvider(descriptor, baseUrl, model, settings.ApiKey);
    }
}
