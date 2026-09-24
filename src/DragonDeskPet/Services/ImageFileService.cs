using System.Drawing;
using System.IO;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class ImageFileService : IImageFileService
{
    public const long MaximumSourceFileBytes = 32L * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    public bool CanLoad(string path) =>
        File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path));

    public Task<CapturedScreenshot> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(path, cancellationToken), cancellationToken);

    internal static CapturedScreenshot Load(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("图片文件不存在。", path);
        }

        if (!SupportedExtensions.Contains(file.Extension))
        {
            throw new InvalidDataException("暂不支持这种图片格式，请使用 PNG、JPG、BMP、GIF 或 TIFF。");
        }

        if (file.Length > MaximumSourceFileBytes)
        {
            throw new InvalidDataException("图片源文件超过 32 MB，请选择更小的图片。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var image = Image.FromFile(file.FullName, useEmbeddedColorManagement: false);
        return ImageInputProcessor.Encode(image);
    }
}

internal static class ImageInputProcessor
{
    private const long MaximumSourcePixels = 120_000_000;

    public static CapturedScreenshot Encode(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if ((long)image.Width * image.Height > MaximumSourcePixels)
        {
            throw new InvalidDataException("图片尺寸过大，请选择更小的图片。");
        }

        using var bitmap = new Bitmap(image);
        return ScreenshotImageProcessor.Encode(
            bitmap,
            ScreenshotCaptureService.MaximumImageEdge,
            ScreenshotCaptureService.MaximumPngBytes);
    }
}
