using DragonDeskPet.AI;

namespace DragonDeskPet.Core;

public enum ScreenshotCaptureStatus
{
    Completed,
    Canceled,
    Failed
}

public sealed class CapturedScreenshot : IDisposable
{
    private byte[]? _pngBytes;

    public CapturedScreenshot(byte[] pngBytes, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pngBytes.Length == 0)
        {
            throw new ArgumentException("截图数据不能为空。", nameof(pngBytes));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "截图尺寸必须大于零。");
        }

        _pngBytes = pngBytes;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int ByteLength => _pngBytes?.Length ?? 0;
    public ReadOnlyMemory<byte> PngBytes => _pngBytes ?? ReadOnlyMemory<byte>.Empty;

    public AiImageAttachment CreateAttachment()
    {
        ObjectDisposedException.ThrowIf(_pngBytes is null, this);
        return new AiImageAttachment("image/png", _pngBytes, Width, Height);
    }

    public void Dispose()
    {
        if (_pngBytes is null)
        {
            return;
        }

        Array.Clear(_pngBytes);
        _pngBytes = null;
    }
}

public sealed record ScreenshotCaptureResult(
    ScreenshotCaptureStatus Status,
    CapturedScreenshot? Screenshot = null,
    string? ErrorMessage = null)
{
    public static ScreenshotCaptureResult Completed(CapturedScreenshot screenshot) =>
        new(ScreenshotCaptureStatus.Completed, screenshot);

    public static ScreenshotCaptureResult Canceled() =>
        new(ScreenshotCaptureStatus.Canceled);

    public static ScreenshotCaptureResult Failed(string message) =>
        new(ScreenshotCaptureStatus.Failed, ErrorMessage: message);
}

public interface IScreenshotCaptureService
{
    Task<ScreenshotCaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default);
}
