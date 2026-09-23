using System.IO;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public static class AssetService
{
    public static string CharacterDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "assets",
        "character");

    public static string CharacterPath => Path.Combine(CharacterDirectory, "default.png");

    public static string IconPath => Path.Combine(
        AppContext.BaseDirectory,
        "assets",
        "icons",
        "app.ico");

    public static string GetCharacterPath(PetState state) => GetCharacterPath(state, CharacterDirectory);

    public static string GetCharacterPath(PetState state, string characterDirectory) => Path.Combine(
        characterDirectory,
        state switch
        {
            PetState.Hover => "hover.png",
            PetState.Dragged => "dragged.png",
            PetState.Thinking => "thinking.png",
            PetState.Happy => "happy.png",
            PetState.Angry => "angry.png",
            PetState.Sleeping => "sleeping.png",
            _ => "default.png"
        });

    public static T LoadCharacterAsset<T>(
        PetState state,
        Func<string, T> loader,
        string? characterDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var directory = characterDirectory ?? CharacterDirectory;
        var preferredPath = GetCharacterPath(state, directory);
        if (state == PetState.Idle)
        {
            return loader(preferredPath);
        }

        try
        {
            return loader(preferredPath);
        }
        catch (Exception exception) when (IsRecoverableAssetFailure(exception))
        {
            return loader(GetCharacterPath(PetState.Idle, directory));
        }
    }

    private static bool IsRecoverableAssetFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or NotSupportedException
            or ArgumentException;
}
