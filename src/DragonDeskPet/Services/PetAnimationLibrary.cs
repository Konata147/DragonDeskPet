using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed record LoadedPetAnimation(PetAnimationClip Clip, BitmapSource[] Frames);

/// <summary>Local bounded decoded-frame cache. Optional artwork cannot prevent startup.</summary>
public sealed class PetAnimationLibrary
{
    private readonly string _directory;
    private readonly Dictionary<string, PetAnimationClip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LoadedPetAnimation> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _cacheOrder = new();
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    public PetAnimationLibrary(string? directory = null)
    {
        _directory = directory ?? Path.Combine(AssetService.CharacterDirectory, "animations");
        try
        {
            var clips = JsonSerializer.Deserialize<PetAnimationClip[]>(File.ReadAllText(Path.Combine(_directory, "clips.json"))) ?? [];
            foreach (var clip in clips.Where(c => c.IsValid)) _clips.TryAdd(clip.Id, clip);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
    }
    public LoadedPetAnimation? Load(string id)
    {
        if (_cache.TryGetValue(id, out var cached)) return cached;
        if (_failed.Contains(id) || !_clips.TryGetValue(id, out var clip)) return null;
        try
        {
            var frames = clip.Frames.Select(path =>
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelHeight = 512;
                bitmap.UriSource = new Uri(Path.GetFullPath(Path.Combine(_directory, path)));
                bitmap.EndInit(); bitmap.Freeze(); return (BitmapSource)bitmap;
            }).ToArray();
            if (frames.Any(f => f.PixelWidth != frames[0].PixelWidth || f.PixelHeight != frames[0].PixelHeight))
                throw new InvalidDataException("Animation frames must share one canvas.");
            while (_cache.Count >= 3) _cache.Remove(_cacheOrder.Dequeue());
            var loaded = new LoadedPetAnimation(clip, frames);
            _cache.Add(id, loaded); _cacheOrder.Enqueue(id); return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _failed.Add(id); return null; }
    }
}
