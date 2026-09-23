namespace DragonDeskPet.AI;

public sealed class UnavailableAiProvider(string displayName, string message) : IAiProvider
{
    public string DisplayName { get; } = displayName;
    public bool IsConfigured => false;

    public Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default) =>
        Task.FromResult(message);
}
