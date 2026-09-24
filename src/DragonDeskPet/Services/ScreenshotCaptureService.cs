using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using DragonDeskPet.Core;
using Application = System.Windows.Application;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using FormsCursor = System.Windows.Forms.Cursors;

namespace DragonDeskPet.Services;

public sealed class ScreenshotCaptureService : IScreenshotCaptureService
{
    public const int MinimumSelectionSize = 16;
    public const int MaximumImageEdge = 2560;
    public const int MaximumPngBytes = 8 * 1024 * 1024;

    public async Task<ScreenshotCaptureResult> CaptureRegionAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ScreenshotCaptureResult.Canceled();
        }

        var completion = new TaskCompletionSource<DrawingRectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new SelectionCoordinator(completion);
        var overlays = Screen.AllScreens
            .Select(screen => new SelectionOverlayForm(screen.Bounds, coordinator))
            .ToArray();
        if (overlays.Length == 0)
        {
            return ScreenshotCaptureResult.Failed("没有检测到可用的显示器。");
        }

        coordinator.SetOverlays(overlays);

        using var registration = cancellationToken.Register(() =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                coordinator.Cancel();
            }
            else
            {
                dispatcher.BeginInvoke(coordinator.Cancel);
            }
        });

        try
        {
            foreach (var overlay in overlays)
            {
                if (completion.Task.IsCompleted)
                {
                    break;
                }

                overlay.Show();
            }

            var selectedRegion = await completion.Task;
            if (selectedRegion is null)
            {
                return ScreenshotCaptureResult.Canceled();
            }

            // Let DWM remove the selection overlays before copying screen pixels.
            await Task.Delay(90, CancellationToken.None);
            try
            {
                return ScreenshotCaptureResult.Completed(
                    ScreenshotImageProcessor.Capture(selectedRegion.Value, MaximumImageEdge, MaximumPngBytes));
            }
            catch (ScreenshotImageTooLargeException)
            {
                return ScreenshotCaptureResult.Failed("截图超过 8 MB，请选择更小的区域后重试。");
            }
        }
        catch (OperationCanceledException)
        {
            return ScreenshotCaptureResult.Canceled();
        }
        finally
        {
            coordinator.CloseOverlays();
            foreach (var overlay in overlays)
            {
                overlay.Dispose();
            }
        }
    }
}

public static class ScreenshotSelection
{
    public static DrawingRectangle NormalizeAndClamp(
        DrawingPoint start,
        DrawingPoint current,
        DrawingRectangle screenBounds)
    {
        var left = Math.Clamp(Math.Min(start.X, current.X), screenBounds.Left, screenBounds.Right);
        var top = Math.Clamp(Math.Min(start.Y, current.Y), screenBounds.Top, screenBounds.Bottom);
        var right = Math.Clamp(Math.Max(start.X, current.X), screenBounds.Left, screenBounds.Right);
        var bottom = Math.Clamp(Math.Max(start.Y, current.Y), screenBounds.Top, screenBounds.Bottom);
        return DrawingRectangle.FromLTRB(left, top, right, bottom);
    }

    public static bool IsLargeEnough(DrawingRectangle rectangle, int minimumSize) =>
        rectangle.Width >= minimumSize && rectangle.Height >= minimumSize;
}

public static class ScreenshotImageProcessor
{
    public static DrawingSize CalculateScaledSize(DrawingSize source, int maximumEdge)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        var longestEdge = Math.Max(source.Width, source.Height);
        if (longestEdge <= maximumEdge)
        {
            return source;
        }

        var scale = maximumEdge / (double)longestEdge;
        return new DrawingSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    public static CapturedScreenshot Capture(DrawingRectangle region, int maximumEdge, int maximumBytes)
    {
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        using var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.Location, DrawingPoint.Empty, region.Size, CopyPixelOperation.SourceCopy);
        }

        return Encode(bitmap, maximumEdge, maximumBytes);
    }

    public static CapturedScreenshot Encode(Bitmap source, int maximumEdge, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        var outputSize = CalculateScaledSize(source.Size, maximumEdge);
        using var resized = new Bitmap(outputSize.Width, outputSize.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new DrawingRectangle(DrawingPoint.Empty, outputSize));
        }

        using var stream = new MemoryStream();
        resized.Save(stream, ImageFormat.Png);
        if (stream.Length > maximumBytes)
        {
            throw new ScreenshotImageTooLargeException();
        }

        return new CapturedScreenshot(stream.ToArray(), outputSize.Width, outputSize.Height);
    }
}

public sealed class ScreenshotImageTooLargeException : Exception;

internal sealed class SelectionCoordinator : IDisposable
{
    private readonly TaskCompletionSource<DrawingRectangle?> _completion;
    private SelectionOverlayForm[] _overlays = [];
    private bool _finished;

    public SelectionCoordinator(TaskCompletionSource<DrawingRectangle?> completion)
    {
        _completion = completion;
    }

    public void SetOverlays(SelectionOverlayForm[] overlays) => _overlays = overlays;

    public void Complete(SelectionOverlayForm source, DrawingPoint start, DrawingPoint current)
    {
        if (_finished)
        {
            return;
        }

        var bounds = source.ScreenBounds;
        var absoluteStart = new DrawingPoint(bounds.Left + start.X, bounds.Top + start.Y);
        var absoluteCurrent = new DrawingPoint(bounds.Left + current.X, bounds.Top + current.Y);
        var selection = ScreenshotSelection.NormalizeAndClamp(absoluteStart, absoluteCurrent, bounds);
        if (!ScreenshotSelection.IsLargeEnough(selection, ScreenshotCaptureService.MinimumSelectionSize))
        {
            source.ResetSelection("区域太小，请重新选择（至少 16×16）");
            return;
        }

        _finished = true;
        CloseOverlays();
        _completion.TrySetResult(selection);
    }

    public void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        CloseOverlays();
        _completion.TrySetResult(null);
    }

    public void CloseOverlays()
    {
        foreach (var overlay in _overlays)
        {
            if (!overlay.IsDisposed)
            {
                overlay.Close();
            }
        }
    }

    public void Dispose() => CloseOverlays();
}

internal sealed class SelectionOverlayForm : Form
{
    private readonly SelectionCoordinator _coordinator;
    private DrawingPoint _start;
    private DrawingPoint _current;
    private bool _selecting;
    private SelectionValidationHintForm? _validationHint;
    private string _message = "拖动选择截图区域 · Esc 或右键取消";

    public SelectionOverlayForm(DrawingRectangle bounds, SelectionCoordinator coordinator)
    {
        ScreenBounds = bounds;
        _coordinator = coordinator;
        Bounds = bounds;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        Opacity = 0.4;
        Cursor = FormsCursor.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
    }

    public DrawingRectangle ScreenBounds { get; }

    protected override bool ShowWithoutActivation => false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            _coordinator.Cancel();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            _coordinator.Cancel();
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            CloseValidationHint();
            _start = e.Location;
            _current = e.Location;
            _selecting = true;
            Capture = true;
            _message = "松开鼠标完成截图";
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_selecting)
        {
            _current = e.Location;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_selecting && e.Button == MouseButtons.Left)
        {
            _current = e.Location;
            _selecting = false;
            Capture = false;
            _coordinator.Complete(this, _start, _current);
        }

        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var textBackground = new SolidBrush(Color.FromArgb(215, 54, 42, 75));
        using var textBrush = new SolidBrush(Color.White);
        using var font = new Font("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        var textSize = e.Graphics.MeasureString(_message, font);
        var textBox = new DrawingRectangle(18, 18, (int)Math.Ceiling(textSize.Width) + 24, 34);
        e.Graphics.FillRectangle(textBackground, textBox);
        e.Graphics.DrawString(_message, font, textBrush, 30, 27);

        if (!_selecting)
        {
            return;
        }

        var localBounds = new DrawingRectangle(0, 0, ClientSize.Width, ClientSize.Height);
        var selection = ScreenshotSelection.NormalizeAndClamp(_start, _current, localBounds);
        using var selectionFill = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
        using var selectionBorder = new Pen(Color.FromArgb(255, 210, 184, 255), 3);
        e.Graphics.FillRectangle(selectionFill, selection);
        e.Graphics.DrawRectangle(selectionBorder, selection);

        var sizeText = $"{selection.Width} × {selection.Height}";
        var sizePoint = new DrawingPoint(selection.Left + 8, Math.Max(60, selection.Top + 8));
        e.Graphics.DrawString(sizeText, font, textBrush, sizePoint);
    }

    public void ResetSelection(string message)
    {
        _selecting = false;
        Capture = false;
        _message = message;
        CloseValidationHint();
        _validationHint = new SelectionValidationHintForm(ScreenBounds, message);
        _validationHint.Show();
        Invalidate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CloseValidationHint();
        base.OnFormClosed(e);
    }

    private void CloseValidationHint()
    {
        if (_validationHint is null)
        {
            return;
        }

        _validationHint.Close();
        _validationHint.Dispose();
        _validationHint = null;
    }
}

internal sealed class SelectionValidationHintForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    public SelectionValidationHintForm(DrawingRectangle screenBounds, string message)
    {
        var width = Math.Min(560, Math.Max(360, screenBounds.Width - 48));
        const int height = 92;
        Bounds = new DrawingRectangle(
            screenBounds.Left + (screenBounds.Width - width) / 2,
            screenBounds.Top + (screenBounds.Height - height) / 2,
            width,
            height);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(75, 51, 105);
        Opacity = 0.97;
        Padding = new Padding(22, 14, 22, 14);

        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 20, FontStyle.Bold, GraphicsUnit.Pixel)
        });
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }
}
