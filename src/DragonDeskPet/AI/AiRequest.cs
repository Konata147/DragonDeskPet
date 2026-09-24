namespace DragonDeskPet.AI;

public sealed record AiRequest(string Prompt, AiImageAttachment? Image = null);

public sealed record AiImageAttachment(
    string MimeType,
    ReadOnlyMemory<byte> Data,
    int Width,
    int Height);
