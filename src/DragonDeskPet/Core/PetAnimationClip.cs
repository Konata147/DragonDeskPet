namespace DragonDeskPet.Core;

public sealed class PetAnimationClip
{
    public string Id { get; set; } = "";
    public string[] Frames { get; set; } = [];
    public int[] DurationsMs { get; set; } = [];
    public int PosterFrame { get; set; }
    public int DurationMs => DurationsMs.Sum();
    public bool IsValid => !string.IsNullOrWhiteSpace(Id) && Frames.Length is > 0 and <= 64
        && Frames.Length == DurationsMs.Length && PosterFrame >= 0 && PosterFrame < Frames.Length
        && DurationsMs.All(t => t is >= 16 and <= 5000)
        && Frames.All(p => !string.IsNullOrWhiteSpace(p) && !System.IO.Path.IsPathRooted(p)
            && !p.Contains(':') && !p.Split('/', '\\').Contains(".."));
    public int FrameAt(long elapsedMilliseconds)
    {
        long boundary = 0;
        for (var i = 0; i < DurationsMs.Length; i++)
        {
            boundary += DurationsMs[i];
            if (elapsedMilliseconds < boundary) return i;
        }
        return Frames.Length - 1;
    }
}
