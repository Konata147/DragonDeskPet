namespace DragonDeskPet.AI;

public sealed record AiProviderDescriptor(
    string Id,
    string DisplayName,
    string DefaultBaseUrl,
    string DefaultModel,
    bool UsesOpenAiCompatibleTransport,
    bool RequiresApiKey);
