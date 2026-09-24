using DragonDeskPet.AI;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

if (args is ["--single-instance-signal", var instanceName])
{
    using var instance = new SingleInstanceService(instanceName);
    if (instance.IsPrimaryInstance)
    {
        return 2;
    }

    return await instance.SignalPrimaryAsync(TimeSpan.FromSeconds(3)) ? 0 : 3;
}

var failures = new List<string>();

await RunAsync("state machine transitions exactly once", () =>
{
    var machine = new PetStateMachine();
    var transitions = 0;
    machine.StateChanged += (_, _) => transitions++;
    machine.TransitionTo(PetState.Hover);
    machine.TransitionTo(PetState.Hover);
    Assert(machine.Current == PetState.Hover, "state should be Hover");
    Assert(transitions == 1, "duplicate transitions should not emit events");
    return Task.CompletedTask;
});

await RunAsync("fullscreen detection recognizes complete monitor coverage", () =>
{
    var primaryMonitor = new Rectangle(0, 0, 1920, 1080);
    var leftMonitor = new Rectangle(-1920, 0, 1920, 1080);

    Assert(
        FullscreenDetectionService.CoversMonitor(new Rectangle(0, 0, 1920, 1080), primaryMonitor),
        "exact monitor coverage should count as fullscreen");
    Assert(
        FullscreenDetectionService.CoversMonitor(new Rectangle(-1928, -8, 1936, 1096), leftMonitor),
        "overscan and negative monitor coordinates should count as fullscreen");
    Assert(
        !FullscreenDetectionService.CoversMonitor(new Rectangle(0, 0, 1920, 1040), primaryMonitor),
        "a maximized work-area window should not count as fullscreen");
    Assert(
        !FullscreenDetectionService.CoversMonitor(new Rectangle(100, 100, 1200, 800), primaryMonitor),
        "an ordinary window should not count as fullscreen");
    return Task.CompletedTask;
});

await RunAsync("all seven state assets are mapped and transparent", () =>
{
    var expectedNames = new Dictionary<PetState, string>
    {
        [PetState.Idle] = "default.png",
        [PetState.Hover] = "hover.png",
        [PetState.Dragged] = "dragged.png",
        [PetState.Thinking] = "thinking.png",
        [PetState.Happy] = "happy.png",
        [PetState.Angry] = "angry.png",
        [PetState.Sleeping] = "sleeping.png"
    };

    foreach (var (state, expectedName) in expectedNames)
    {
        var path = AssetService.GetCharacterPath(state);
        Assert(Path.GetFileName(path) == expectedName, $"{state} should map to {expectedName}");
        Assert(File.Exists(path), $"missing state asset {path}");
        using var bitmap = new Bitmap(path);
        Assert(bitmap.Width == 1241 && bitmap.Height == 1268, $"{expectedName} has an unexpected size");
        Assert(Image.IsAlphaPixelFormat(bitmap.PixelFormat), $"{expectedName} must have an alpha channel");
        Assert(bitmap.GetPixel(0, 0).A == 0, $"{expectedName} top-left corner must be transparent");
    }

    return Task.CompletedTask;
});

await RunAsync("missing or corrupt state assets fall back independently", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "default.png"), "idle-ok");
        File.WriteAllText(Path.Combine(directory, "hover.png"), "corrupt");

        string Loader(string path)
        {
            var content = File.ReadAllText(path);
            if (content == "corrupt")
            {
                throw new InvalidDataException("test corrupt asset");
            }

            return content;
        }

        var corruptFallback = AssetService.LoadCharacterAsset(PetState.Hover, Loader, directory);
        var missingFallback = AssetService.LoadCharacterAsset(PetState.Angry, Loader, directory);
        Assert(corruptFallback == "idle-ok", "corrupt state should fall back to idle");
        Assert(missingFallback == "idle-ok", "missing state should fall back to idle");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    return Task.CompletedTask;
});

await RunAsync("provider catalog keeps the planned providers", () =>
{
    var expected = new[] { "OpenAI", "Gemini", "Anthropic", "DeepSeek", "OpenRouter", "Ollama", "LMStudio", "Custom" };
    foreach (var provider in expected)
    {
        Assert(AiProviderCatalog.All.Any(item => item.Id == provider), $"missing provider {provider}");
    }

    var deepSeek = AiProviderCatalog.Find("DeepSeek");
    Assert(deepSeek.DefaultModel == "deepseek-flash", "DeepSeek should default to its vision-capable model");
    Assert(deepSeek.DefaultBaseUrl == "https://api.deepseek.com", "DeepSeek should use the documented API base URL");

    return Task.CompletedTask;
});

await RunAsync("offline provider gives a useful local response", async () =>
{
    var provider = new AiProviderFactory().Create(new AppSettings());
    var response = await provider.SendAsync(new AiRequest("hello"));
    Assert(!provider.IsConfigured, "offline provider must not report configured");
    Assert(response.Contains("设置", StringComparison.Ordinal), "offline response should direct the user to settings");
});

await RunAsync("screenshot selections normalize and stay on their starting screen", () =>
{
    var secondaryScreen = new Rectangle(-1920, -120, 1920, 1080);
    var forward = ScreenshotSelection.NormalizeAndClamp(
        new Point(-1800, 20),
        new Point(-900, 620),
        secondaryScreen);
    var reverse = ScreenshotSelection.NormalizeAndClamp(
        new Point(-900, 620),
        new Point(-1800, 20),
        secondaryScreen);
    var crossScreen = ScreenshotSelection.NormalizeAndClamp(
        new Point(-1800, 20),
        new Point(500, 1400),
        secondaryScreen);

    Assert(forward == reverse, "reverse selection should normalize to the same rectangle");
    Assert(forward == new Rectangle(-1800, 20, 900, 600), "normalized selection is incorrect");
    Assert(crossScreen.Right == secondaryScreen.Right, "selection must clamp to the starting screen right edge");
    Assert(crossScreen.Bottom == secondaryScreen.Bottom, "selection must clamp to the starting screen bottom edge");
    Assert(!ScreenshotSelection.IsLargeEnough(new Rectangle(0, 0, 15, 16), 16), "too-small selection must be rejected");
    Assert(ScreenshotSelection.IsLargeEnough(new Rectangle(0, 0, 16, 16), 16), "minimum selection should be accepted");
    return Task.CompletedTask;
});

await RunAsync("screenshot PNG processing resizes and enforces the byte limit", () =>
{
    var scaled = ScreenshotImageProcessor.CalculateScaledSize(new Size(3000, 1500), 2560);
    Assert(scaled == new Size(2560, 1280), "longest edge should be reduced to 2560");

    using var source = new Bitmap(300, 150);
    using (var graphics = Graphics.FromImage(source))
    {
        graphics.Clear(Color.MediumPurple);
    }

    using var encoded = ScreenshotImageProcessor.Encode(source, 2560, 8 * 1024 * 1024);
    Assert(encoded.Width == 300 && encoded.Height == 150, "small images should retain their dimensions");
    Assert(encoded.ByteLength > 8, "PNG output should contain data");
    Assert(encoded.PngBytes.Span[0] == 0x89 && encoded.PngBytes.Span[1] == 0x50, "output should be a PNG");

    var rejected = false;
    try
    {
        using var ignored = ScreenshotImageProcessor.Encode(source, 2560, 1);
    }
    catch (ScreenshotImageTooLargeException)
    {
        rejected = true;
    }

    Assert(rejected, "encoded images above the configured byte limit should be rejected");
    return Task.CompletedTask;
});

await RunAsync("captured screenshots clear their memory when disposed", () =>
{
    var bytes = new byte[] { 1, 2, 3, 4 };
    var screenshot = new CapturedScreenshot(bytes, 1, 1);
    var attachment = screenshot.CreateAttachment();
    screenshot.Dispose();
    Assert(screenshot.ByteLength == 0, "disposed screenshot should no longer expose data");
    Assert(attachment.Data.Span.ToArray().All(value => value == 0), "owned screenshot bytes should be cleared");
    return Task.CompletedTask;
});

await RunAsync("clipboard text is read only on request and retries temporary locks", async () =>
{
    var backend = new StubClipboardBackend(text: "  clipboard question  ", lockedAttempts: 2);
    var service = new ClipboardContentService(backend);
    var result = await service.ReadAsync();
    Assert(result.Kind == ClipboardContentKind.Text, "clipboard text should be recognized");
    Assert(result.Text == "clipboard question", "clipboard text should be trimmed");
    Assert(backend.ContainsImageCalls == 3, "clipboard should retry two temporary lock failures");
});

await RunAsync("clipboard images become bounded in-memory PNG attachments", async () =>
{
    var backend = new StubClipboardBackend(imageFactory: () =>
    {
        var bitmap = new Bitmap(3000, 1500);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.CornflowerBlue);
        return bitmap;
    });
    var service = new ClipboardContentService(backend);
    var result = await service.ReadAsync();
    Assert(result.Kind == ClipboardContentKind.Image && result.Image is not null, "clipboard image should be recognized");
    using var image = result.Image ?? throw new InvalidOperationException("clipboard image result was null");
    Assert(image.Width == 2560 && image.Height == 1280, "clipboard image should respect the longest-edge limit");
    Assert(image.ByteLength <= ScreenshotCaptureService.MaximumPngBytes, "clipboard PNG should respect the byte limit");
});

await RunAsync("clipboard image file copies are recognized", async () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, "copied-image.png");
        using (var bitmap = new Bitmap(240, 120))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Plum);
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        var backend = new StubClipboardBackend(fileDropPaths: [imagePath]);
        var service = new ClipboardContentService(backend, new ImageFileService());
        var result = await service.ReadAsync();
        Assert(result.Kind == ClipboardContentKind.Image && result.Image is not null, "copied image file should become an image result");
        using var image = result.Image ?? throw new InvalidOperationException("copied image file result was null");
        Assert(image.Width == 240 && image.Height == 120, "copied image file dimensions should be retained");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
});

await RunAsync("dropped image files are validated and loaded without persistence", async () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var imagePath = Path.Combine(directory, "dropped.png");
        using (var bitmap = new Bitmap(3200, 1600))
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.MediumSlateBlue);
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
        }

        var textPath = Path.Combine(directory, "not-an-image.txt");
        File.WriteAllText(textPath, "not an image");
        var service = new ImageFileService();
        Assert(service.CanLoad(imagePath), "PNG should be accepted for drag-and-drop");
        Assert(!service.CanLoad(textPath), "non-image files should be rejected");
        using var loaded = await service.LoadAsync(imagePath);
        Assert(loaded.Width == 2560 && loaded.Height == 1280, "dropped image should be resized proportionally");
        Assert(loaded.ByteLength <= ScreenshotCaptureService.MaximumPngBytes, "dropped image should respect the byte limit");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
});

await RunAsync("OpenAI-compatible requests preserve text and encode image content", () =>
{
    var textJson = OpenAiCompatibleProvider.SerializeRequest("vision-model", new AiRequest("hello"));
    using (var textDocument = JsonDocument.Parse(textJson))
    {
        var content = textDocument.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert(content.ValueKind == JsonValueKind.String && content.GetString() == "hello", "text-only content format must remain a string");
    }

    var image = new AiImageAttachment("image/png", new byte[] { 1, 2, 3 }, 2, 3);
    var imageJson = OpenAiCompatibleProvider.SerializeRequest("vision-model", new AiRequest("inspect", image));
    using var imageDocument = JsonDocument.Parse(imageJson);
    var imageContent = imageDocument.RootElement.GetProperty("messages")[1].GetProperty("content");
    Assert(imageContent.ValueKind == JsonValueKind.Array && imageContent.GetArrayLength() == 2, "image request should use a two-part content array");
    Assert(imageContent[0].GetProperty("text").GetString() == "inspect", "image request should retain prompt text");
    Assert(
        imageContent[1].GetProperty("image_url").GetProperty("url").GetString() == "data:image/png;base64,AQID",
        "image request should contain the expected in-memory PNG data URI");
    return Task.CompletedTask;
});

await RunAsync("unsupported image responses are friendly and do not leak secrets", async () =>
{
    const string secret = "sk-image-test-secret";
    using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
    {
        Content = new StringContent(
            $"{{\"error\":{{\"message\":\"Authorization: Bearer {secret} data:image/png;base64,AQID\"}}}}",
            Encoding.UTF8,
            "application/json")
    }));
    var descriptor = AiProviderCatalog.Find("Custom");
    var provider = new OpenAiCompatibleProvider(descriptor, "https://example.invalid/v1", "vision-model", secret, client);
    try
    {
        await provider.SendAsync(new AiRequest(
            "inspect",
            new AiImageAttachment("image/png", new byte[] { 1, 2, 3 }, 2, 3)));
        throw new InvalidOperationException("image request should have failed");
    }
    catch (InvalidOperationException ex)
    {
        Assert(ex.Message.Contains("可能不支持图片输入", StringComparison.Ordinal), "image error should explain likely model incompatibility");
        Assert(!ex.Message.Contains(secret, StringComparison.Ordinal), "image error must not leak the API key");
        Assert(!ex.Message.Contains("base64", StringComparison.OrdinalIgnoreCase), "image error must not leak encoded image data");
    }
});

await RunAsync("OpenAI-compatible provider configuration is vendor-neutral", () =>
{
    var settings = new AppSettings
    {
        Provider = "Custom",
        BaseUrl = "http://127.0.0.1:1234/v1",
        Model = "local-model"
    };
    var provider = new AiProviderFactory().Create(settings);
    Assert(provider is OpenAiCompatibleProvider, "custom provider should use the compatible transport");
    Assert(provider.IsConfigured, "local compatible providers should not require an API key");
    return Task.CompletedTask;
});

await RunAsync("Windows user encryption round-trips Unicode secrets", () =>
{
    const string secret = "sk-local-测试-123";
    var protectedValue = WindowsSecretProtector.Protect(secret);
    Assert(protectedValue != secret, "stored value must not be plaintext");
    Assert(WindowsSecretProtector.Unprotect(protectedValue) == secret, "decrypted secret must match");
    return Task.CompletedTask;
});

await RunAsync("portable settings persist without plaintext keys", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new SettingsService(directory);
        service.Save(new AppSettings
        {
            Scale = 3.5,
            HasCompletedOnboarding = true,
            Provider = "OpenAI",
            Model = "test-model",
            BaseUrl = "https://example.invalid/v1",
            ApiKey = "top-secret-value"
        });

        var raw = File.ReadAllText(service.SettingsPath);
        Assert(!raw.Contains("top-secret-value", StringComparison.Ordinal), "settings JSON must not contain plaintext keys");

        var loaded = service.Load();
        Assert(loaded.Scale == 2.0, "scale should be clamped to its safe maximum");
        Assert(loaded.ApiKey == "top-secret-value", "encrypted key should load for the same Windows user");
        Assert(loaded.Provider == "OpenAI", "provider should persist");
        Assert(loaded.HasCompletedOnboarding, "onboarding completion should persist");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    return Task.CompletedTask;
});

await RunAsync("legacy settings default onboarding to incomplete", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var service = new SettingsService(directory);
        File.WriteAllText(service.SettingsPath, "{\"Scale\":1.1,\"Provider\":\"Offline\"}");
        var loaded = service.Load();
        Assert(!loaded.HasCompletedOnboarding, "legacy settings must default onboarding to false");
        Assert(loaded.AutoHideInFullscreen, "legacy settings must default fullscreen auto-hide to true");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    return Task.CompletedTask;
});

await RunAsync("second process signals show and exits", async () =>
{
    var name = $"DragonDeskPet.Smoke.{Guid.NewGuid():N}";
    using var primary = new SingleInstanceService(name);
    Assert(primary.IsPrimaryInstance, "first service should be primary");
    var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    primary.ShowRequested += (_, _) => shown.TrySetResult(true);

    var executable = Environment.ProcessPath ?? throw new InvalidOperationException("test executable path is unavailable");
    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("--single-instance-signal");
    startInfo.ArgumentList.Add(name);
    using var secondary = Process.Start(startInfo) ?? throw new InvalidOperationException("secondary test process did not start");
    await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Assert(secondary.ExitCode == 0, $"secondary process exit code should be 0, actual {secondary.ExitCode}");
    Assert(await shown.Task.WaitAsync(TimeSpan.FromSeconds(3)), "primary should receive show");
});

await RunAsync("crash logs redact secrets and retain only ten", () =>
{
    const string secret = "test-api-key-DO-NOT-LOG";
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var service = new CrashLogService(directory);
        for (var index = 0; index < 12; index++)
        {
            service.Write(
                new InvalidOperationException($"api_key={secret} Authorization: Bearer {secret} failure {index}"),
                secret);
        }

        var logs = Directory.GetFiles(service.LogDirectory, "crash-*.txt");
        Assert(logs.Length == 10, "crash log retention should keep exactly ten files");
        foreach (var log in logs)
        {
            var content = File.ReadAllText(log);
            Assert(!content.Contains(secret, StringComparison.Ordinal), "crash log must redact the API key");
            Assert(!content.Contains("Source:", StringComparison.Ordinal), "crash log must contain only approved fields");
            Assert(content.Contains("Time:", StringComparison.Ordinal), "crash log should include time");
            Assert(content.Contains("Version:", StringComparison.Ordinal), "crash log should include version");
            Assert(content.Contains("Exception:", StringComparison.Ordinal), "crash log should include exception type");
            Assert(content.Contains("Message:", StringComparison.Ordinal), "crash log should include message");
            Assert(content.Contains("Stack trace:", StringComparison.Ordinal), "crash log should include stack trace");
        }
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    return Task.CompletedTask;
});

await RunAsync("application icon contains seven standard sizes", () =>
{
    Assert(File.Exists(AssetService.IconPath), "application icon should exist");
    var bytes = File.ReadAllBytes(AssetService.IconPath);
    Assert(bytes.Length >= 6, "icon header is incomplete");
    var count = BitConverter.ToUInt16(bytes, 4);
    Assert(count == 7, "icon should contain seven image entries");
    var expectedSizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
    var actualSizes = new List<int>();
    for (var index = 0; index < count; index++)
    {
        var offset = 6 + index * 16;
        var width = bytes[offset] == 0 ? 256 : bytes[offset];
        var height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
        Assert(width == height, "icon entries should be square");
        actualSizes.Add(width);
    }

    Assert(actualSizes.SequenceEqual(expectedSizes), "icon sizes should be 16, 24, 32, 48, 64, 128 and 256");
    return Task.CompletedTask;
});

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("All DragonDeskPet smoke tests passed.");
    return 0;
}

Console.Error.WriteLine($"{failures.Count} smoke test(s) failed:");
foreach (var failure in failures)
{
    Console.Error.WriteLine($"- {failure}");
}

return 1;

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL  {name}");
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(responseFactory(request));
}

sealed class StubClipboardBackend(
    string? text = null,
    Func<Image?>? imageFactory = null,
    IReadOnlyList<string>? fileDropPaths = null,
    int lockedAttempts = 0) : IClipboardBackend
{
    public int ContainsImageCalls { get; private set; }

    public bool ContainsImage()
    {
        ContainsImageCalls++;
        if (ContainsImageCalls <= lockedAttempts)
        {
            throw new ExternalException("clipboard busy");
        }

        return imageFactory is not null;
    }

    public Image? GetImage() => imageFactory?.Invoke();

    public bool ContainsFileDrop() => fileDropPaths is not null;

    public IReadOnlyList<string> GetFileDropPaths() => fileDropPaths ?? [];

    public bool ContainsText() => text is not null;

    public string GetText() => text ?? string.Empty;
}
