namespace DragonDeskPet.AI;

public interface IAiProvider
{
    string DisplayName { get; }
    bool IsConfigured { get; }
    Task<string> SendAsync(string prompt, CancellationToken cancellationToken = default);
}
