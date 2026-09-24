using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class ClipboardContentService : IClipboardContentService
{
    private readonly IClipboardBackend _backend;
    private readonly IImageFileService _imageFileService;

    public ClipboardContentService()
        : this(new WindowsClipboardBackend(), new ImageFileService())
    {
    }

    internal ClipboardContentService(IClipboardBackend backend, IImageFileService? imageFileService = null)
    {
        _backend = backend;
        _imageFileService = imageFileService ?? new ImageFileService();
    }

    public async Task<ClipboardContentResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (_backend.ContainsImage())
                {
                    using var image = _backend.GetImage();
                    if (image is null)
                    {
                        return new ClipboardContentResult(
                            ClipboardContentKind.Failed,
                            ErrorMessage: "剪贴板图片暂时无法读取，请重新复制后再试。");
                    }

                    return new ClipboardContentResult(
                        ClipboardContentKind.Image,
                        Image: ImageInputProcessor.Encode(image));
                }

                if (_backend.ContainsFileDrop())
                {
                    var paths = _backend.GetFileDropPaths();
                    if (paths.Count != 1 || !_imageFileService.CanLoad(paths[0]))
                    {
                        return new ClipboardContentResult(
                            ClipboardContentKind.Failed,
                            ErrorMessage: "请一次复制一张 PNG、JPG、BMP、GIF 或 TIFF 图片。");
                    }

                    return new ClipboardContentResult(
                        ClipboardContentKind.Image,
                        Image: await _imageFileService.LoadAsync(paths[0], cancellationToken));
                }

                if (_backend.ContainsText())
                {
                    var text = _backend.GetText().Trim();
                    return text.Length == 0
                        ? new ClipboardContentResult(ClipboardContentKind.Empty)
                        : new ClipboardContentResult(ClipboardContentKind.Text, Text: text);
                }

                return new ClipboardContentResult(ClipboardContentKind.Empty);
            }
            catch (ExternalException) when (attempt < 2)
            {
                await Task.Delay(60, cancellationToken);
            }
            catch (ScreenshotImageTooLargeException)
            {
                return new ClipboardContentResult(
                    ClipboardContentKind.Failed,
                    ErrorMessage: "剪贴板图片超过 8 MB，请复制更小的图片后重试。");
            }
            catch (InvalidDataException ex)
            {
                return new ClipboardContentResult(ClipboardContentKind.Failed, ErrorMessage: ex.Message);
            }
        }

        return new ClipboardContentResult(
            ClipboardContentKind.Failed,
            ErrorMessage: "剪贴板正被其他程序占用，请稍后再试。");
    }
}

internal interface IClipboardBackend
{
    bool ContainsImage();
    Image? GetImage();
    bool ContainsFileDrop();
    IReadOnlyList<string> GetFileDropPaths();
    bool ContainsText();
    string GetText();
}

internal sealed class WindowsClipboardBackend : IClipboardBackend
{
    public bool ContainsImage() => Clipboard.ContainsImage();
    public Image? GetImage() => Clipboard.GetImage();
    public bool ContainsFileDrop() => Clipboard.ContainsFileDropList();
    public IReadOnlyList<string> GetFileDropPaths() => Clipboard.GetFileDropList().Cast<string>().ToArray();
    public bool ContainsText() => Clipboard.ContainsText(TextDataFormat.UnicodeText);
    public string GetText() => Clipboard.GetText(TextDataFormat.UnicodeText);
}
