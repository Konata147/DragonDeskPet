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

await RunAsync("stale feedback cannot reset a second active drag", () =>
{
    var machine = new PetStateMachine();
    machine.TransitionTo(PetState.Dragged);
    machine.TransitionTo(PetState.Happy);
    var firstDropRevision = machine.Revision;
    machine.TransitionTo(PetState.Dragged);
    Assert(!machine.TryFinishFeedback(firstDropRevision), "first drop timeout must not reset the second drag");
    Assert(machine.Current == PetState.Dragged, "second drag must keep its dragged pose");
    machine.TransitionTo(PetState.Happy);
    var secondDropRevision = machine.Revision;
    Assert(!machine.TryFinishFeedback(firstDropRevision), "first drop timeout must not shorten the second happy feedback");
    Assert(machine.TryFinishFeedback(secondDropRevision) && machine.Current == PetState.Idle, "latest drop timeout should restore idle");
    return Task.CompletedTask;
});

await RunAsync("feedback returns to hover when the pointer still rests on the pet", () =>
{
    var machine = new PetStateMachine();
    machine.TransitionTo(PetState.Happy);
    var revision = machine.Revision;
    Assert(machine.TryFinishFeedback(revision, pointerOverCharacter: true), "current feedback should finish");
    Assert(machine.Current == PetState.Hover, "hovered pet should not remain idle after feedback");
    return Task.CompletedTask;
});

await RunAsync("pet placement keeps the transformed character inside the chosen monitor", () =>
{
    var screen = new Rectangle(0, 0, 1706, 1019);
    var character = new Rectangle(465, 166, 303, 397);
    var right = PetPlacement.ClampWindowTopLeft(new Point(1200, 300), character, screen, 8);
    Assert(right.X + character.Right == screen.Right - 8, "dragging right should keep the entire character visible");
    Assert(right.Y == 300, "horizontal clamping should preserve the vertical position");

    var topLeft = PetPlacement.ClampWindowTopLeft(new Point(-2000, -2000), character, screen, 8);
    Assert(topLeft.X + character.Left == 8 && topLeft.Y + character.Top == 8,
        "dragging beyond the top-left should stop at the character edge, not snap to the center");

    var unchanged = PetPlacement.ClampWindowTopLeft(new Point(100, 200), character, screen, 8);
    Assert(unchanged == new Point(100, 200), "positions that are already visible should not move");

    var leftMonitor = new Rectangle(-1920, 0, 1920, 1080);
    var negativeScreen = PetPlacement.ClampWindowTopLeft(new Point(-3000, 200), character, leftMonitor, 8);
    Assert(negativeScreen.X + character.Left == leftMonitor.Left + 8,
        "a monitor with negative coordinates should use its own left edge");
    return Task.CompletedTask;
});

await RunAsync("pet placement excludes transparent art padding", () =>
{
    var pixels = new byte[4 * 4 * 4];
    pixels[(1 * 4 + 1) * 4 + 3] = 255;
    pixels[(3 * 4 + 2) * 4 + 3] = 255;
    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
        4, 4, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 16);
    var opaque = CharacterArtworkBounds.FindOpaqueNormalizedBounds(bitmap);
    Assert(opaque.Left == 0.25 && opaque.Top == 0.25 && opaque.Right == 0.75 && opaque.Bottom == 1,
        "alpha bounds should exclude fully transparent pixels");
    var fitted = CharacterArtworkBounds.FitNormalizedBounds(
        opaque, new System.Windows.Size(168, 220), new System.Windows.Size(4, 4));
    Assert(fitted.Left == 42 && fitted.Top == 68 && fitted.Right == 126 && fitted.Bottom == 194,
        "image letterboxing and the opaque rectangle should both affect the visible edge");
    return Task.CompletedTask;
});

await RunAsync("pet may cross an edge by half but cannot disappear", () =>
{
    var screen = new Rectangle(0, 0, 1706, 1019);
    var character = new Rectangle(465, 166, 303, 397);
    var right = PetPlacement.ClampWindowTopLeft(new Point(4000, 300), character, screen, 8, 0.5);
    var visibleRightWidth = screen.Right - 8 - (right.X + character.Left);
    Assert(visibleRightWidth >= character.Width / 2.0 && visibleRightWidth < character.Width / 2.0 + 2,
        "right edge should leave half the artwork visible without an extra gap");

    var topLeft = PetPlacement.ClampWindowTopLeft(new Point(-4000, -4000), character, screen, 8, 0.5);
    var visibleLeftWidth = topLeft.X + character.Right - (screen.Left + 8);
    var visibleTopHeight = topLeft.Y + character.Bottom - (screen.Top + 8);
    Assert(visibleLeftWidth >= character.Width / 2.0 && visibleTopHeight >= character.Height / 2.0,
        "even at a corner at least half remains visible on each axis");

    var unchanged = PetPlacement.ClampWindowTopLeft(new Point(400, 200), character, screen, 8, 0.5);
    Assert(unchanged == new Point(400, 200), "positions away from an edge should not snap");
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

await RunAsync("state artwork union leaves only true transparent padding", () =>
{
    var union = System.Windows.Rect.Empty;
    foreach (var state in Enum.GetValues<PetState>())
    {
        var image = new System.Windows.Media.Imaging.BitmapImage(new Uri(AssetService.GetCharacterPath(state)));
        union.Union(CharacterArtworkBounds.FindOpaqueNormalizedBounds(image));
    }

    Assert(union.Left > 0.02 && union.Top > 0 && union.Right < 0.98 && union.Bottom < 1,
        "the seven states should share a bounded, nontransparent artwork region");
    var fitted = CharacterArtworkBounds.FitNormalizedBounds(
        union, new System.Windows.Size(168, 220), new System.Windows.Size(1241, 1268));
    Assert(fitted.Left > 0 && fitted.Top > 0 && fitted.Right < 168 && fitted.Bottom < 220,
        "character placement should not use the full image element rectangle");
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
    var ollama = AiProviderCatalog.Find("Ollama");
    Assert(!ollama.RequiresApiKey, "Ollama should remain available without an API key");
    Assert(ollama.DefaultModel == "qwen2.5vl:3b", "Ollama should default to a lightweight vision model");

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

await RunAsync("strict reminder commands stay local and require unambiguous time", () =>
{
    var now = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(8));
    Assert(ReminderCommandParser.TryParse("20分钟后提醒我休息", now, out var relative, out _), "relative reminder should parse");
    Assert(relative?.Message == "休息" && relative.DueUtc == now.AddMinutes(20).ToUniversalTime(), "relative reminder should use the exact delay");
    Assert(ReminderCommandParser.TryParse("明天 8:30 提醒我上课", now, out var tomorrow, out _), "tomorrow reminder should parse");
    Assert(TimeZoneInfo.ConvertTime(tomorrow!.DueUtc, TimeZoneInfo.Local).Hour == 8, "tomorrow reminder should retain local clock time");
    Assert(ReminderCommandParser.TryParse("每天 22:00 提醒我吃药", now, out var daily, out _), "daily reminder should parse");
    Assert(daily?.Repeat == ReminderRepeat.Daily && daily.DailyLocalTime == new TimeOnly(22, 0), "daily recurrence should be explicit");
    Assert(!ReminderCommandParser.TryParse("下课后提醒我交作业", now, out _, out var error), "ambiguous reminder should not be guessed");
    Assert(!string.IsNullOrWhiteSpace(error), "ambiguous reminder should explain the form fallback");
    return Task.CompletedTask;
});

await RunAsync("reminders recover overdue items and support daily and snooze", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        var service = new ReminderService(store);
        var now = DateTimeOffset.UtcNow;
        service.Create(new ReminderDraft("one time", ReminderRepeat.None, now.AddMinutes(-3)));
        service.Create(new ReminderDraft("daily", ReminderRepeat.Daily, now.AddMinutes(-2), TimeOnly.FromDateTime(DateTime.Now.AddHours(1))));
        service.Tick(now);
        var pending = service.GetPendingAlerts(now);
        Assert(pending.Count == 2, "both overdue reminders should enter the pending queue");
        Assert(service.GetReminders().Count(item => item.IsEnabled) == 1, "only the daily reminder should remain enabled");
        service.Snooze(pending[0].Id, TimeSpan.FromMinutes(10), now);
        Assert(service.GetPendingAlerts(now).Count == 1, "snoozed reminder should be hidden until it is due again");
        Assert(service.GetPendingAlerts(now.AddMinutes(11)).Count == 2, "snoozed reminder should return after its delay");

        var reloaded = new ProductivityStore(directory);
        Assert(reloaded.Data.PendingAlerts.Count == 2, "pending alerts should survive restart");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("todo rollover and seven day archive cleanup are deterministic", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        var today = DateOnly.FromDateTime(DateTime.Today);
        store.Data.Todos.Add(new TodoItem { Text = "roll", AssignedDate = today.AddDays(-1) });
        store.Data.Todos.Add(new TodoItem { Text = "old complete", AssignedDate = today.AddDays(-9), IsCompleted = true, CompletedUtc = DateTimeOffset.UtcNow.AddDays(-8) });
        store.Data.Todos.Add(new TodoItem { Text = "recent complete", AssignedDate = today.AddDays(-2), IsCompleted = true, CompletedUtc = DateTimeOffset.UtcNow.AddDays(-2) });
        store.NormalizeForToday(today);
        Assert(store.Data.Todos.Single(item => item.Text == "roll").AssignedDate == today, "unfinished todo should roll into today");
        Assert(store.Data.Todos.All(item => item.Text != "old complete"), "completed todo older than seven days should be removed");
        Assert(store.Data.Todos.Any(item => item.Text == "recent complete"), "recent completed todo should remain archived");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("todo text editing persists without changing completion state", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        var todos = new TodoService(store);
        var item = todos.Add("清单测试", DateOnly.FromDateTime(DateTime.Today));
        todos.SetCompleted(item.Id, true, DateTimeOffset.UtcNow);
        todos.Update(item.Id, "修改后的清单测试");
        var saved = new ProductivityStore(directory).Data.Todos.Single();
        Assert(saved.Text == "修改后的清单测试" && saved.IsCompleted && saved.Id == item.Id,
            "editing should persist text while keeping the same todo and completion state");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("pomodoro pauses, restores and enters a long break after four rounds", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var settings = new AppSettings { PomodoroFocusMinutes = 25, PomodoroShortBreakMinutes = 5, PomodoroLongBreakMinutes = 15, PomodoroRoundsBeforeLongBreak = 4 };
        var store = new ProductivityStore(directory);
        var service = new PomodoroService(store, () => settings);
        var now = DateTimeOffset.UtcNow;
        service.Start(now);
        service.Pause(now.AddMinutes(5));
        Assert(service.State.IsPaused && Math.Abs(service.State.PausedRemaining.TotalMinutes - 20) < 0.1, "pause should persist the remaining duration");
        service.Resume(now.AddMinutes(10));
        Assert(service.State.IsRunning, "resume should restart the current phase");
        service.State.CompletedFocusRounds = 3;
        service.State.EndsAtUtc = now.AddMinutes(9);
        service.Tick(now.AddMinutes(10));
        Assert(service.State.Phase == PomodoroPhase.LongBreak, "fourth focus round should select a long break");
        Assert(service.State.IsAwaitingNextPhase && !service.State.IsRunning, "next phase must wait for user confirmation");
        var reloaded = new ProductivityStore(directory);
        Assert(reloaded.Data.Pomodoro.IsAwaitingNextPhase, "pomodoro transition state should survive restart");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("custom focus minutes persist and apply only to the next session", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var settingsService = new SettingsService(directory);
        var settings = settingsService.Load();
        settings.PomodoroFocusMinutes = 42;
        settingsService.Save(settings);
        settings = settingsService.Load();
        Assert(settings.PomodoroFocusMinutes == 42, "custom focus length should persist in settings");

        var service = new PomodoroService(new ProductivityStore(directory), () => settings);
        var now = DateTimeOffset.UtcNow;
        service.Start(now);
        Assert(service.GetRemaining(now) == TimeSpan.FromMinutes(42), "new focus should use custom length");
        settings.PomodoroFocusMinutes = 17;
        settingsService.Save(settings);
        Assert(service.GetRemaining(now) == TimeSpan.FromMinutes(42), "changing length must not shorten an active focus");
        service.Cancel();
        service.Start(now);
        Assert(service.GetRemaining(now) == TimeSpan.FromMinutes(17), "next focus should use updated length");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("course schedule respects teaching week parity and skipped dates", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        store.Data.Semester.StartDate = new DateOnly(2026, 9, 7);
        var service = new CourseScheduleService(store);
        var course = new CourseItem
        {
            Name = "高数",
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 40),
            StartWeek = 1,
            EndWeek = 16,
            WeekPattern = CourseWeekPattern.Odd,
            ReminderMinutes = 30
        };
        service.AddOrUpdate(course);
        Assert(service.GetTeachingWeek(new DateOnly(2026, 9, 7)) == 1, "semester start should be teaching week one");
        Assert(service.GetCoursesForDate(new DateOnly(2026, 9, 7)).Count == 1, "odd-week course should appear in week one");
        Assert(service.GetCoursesForDate(new DateOnly(2026, 9, 14)).Count == 0, "odd-week course should not appear in week two");
        service.SkipDate(course.Id, new DateOnly(2026, 9, 21));
        Assert(service.GetCoursesForDate(new DateOnly(2026, 9, 21)).Count == 0, "single-day cancellation should exclude that occurrence");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("CSV import previews without writing and applies merge only after confirmation", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var csv = Path.Combine(directory, "courses.csv");
        File.WriteAllText(csv, "课程名,星期,上课时间,下课时间,地点,教师,开始周,结束周,周次规则,提前提醒分钟,是否启用\n\"大学英语\",三,14:00,15:40,\"B楼,203\",李老师,1,16,单周,20,是", new UTF8Encoding(false));
        var store = new ProductivityStore(directory);
        var importer = new CourseScheduleImporter(store);
        var preview = importer.Preview(csv, store.Data.Courses, store.Data.Semester);
        Assert(preview.AddedCount == 1 && preview.InvalidCount == 0, "valid quoted CSV row should preview as one addition");
        var displayed = new DragonDeskPet.CourseImportDisplayRow(preview.Rows[0]);
        Assert(displayed.ScheduleText.Contains("星期三 14:00–15:40") && displayed.ScheduleText.Contains("单周"),
            "import preview should show the course time and week pattern");
        Assert(displayed.LocationTeacherText.Contains("B楼,203") && displayed.LocationTeacherText.Contains("李老师"),
            "import preview should show the location and teacher");
        Assert(store.Data.Courses.Count == 0 && !File.Exists(store.DataPath), "preview must not persist imported records");
        importer.Apply(preview, false);
        Assert(store.Data.Courses.Count == 1 && store.Data.Courses[0].Location == "B楼,203", "confirmed merge should preserve escaped CSV fields");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("replace import with no valid courses preserves the existing schedule", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        var existing = new CourseItem
        {
            Name = "已保存课程",
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 40)
        };
        store.Data.Courses.Add(existing);
        store.Save();
        var preview = new CourseImportPreview();
        preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Invalid, null, "无效记录", "缺少时间"));

        new CourseScheduleImporter(store).Apply(preview, replaceCurrentSemester: true);

        Assert(store.Data.Courses.Count == 1 && store.Data.Courses[0].Id == existing.Id,
            "an empty replacement must not erase courses in memory");
        Assert(new ProductivityStore(directory).Data.Courses.Single().Id == existing.Id,
            "an empty replacement must not erase courses on disk");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("replace import retains duplicate rows and removes only omitted courses", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        var existing = new CourseItem
        {
            Name = "原有且重复的课",
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 40)
        };
        store.Data.Courses.Add(existing);
        var omitted = new CourseItem { Name = "文件未包含的课" };
        store.Data.Courses.Add(omitted);
        store.Data.PendingAlerts.Add(new PendingAlert
        {
            Source = AlertSource.Course,
            SourceId = omitted.Id,
            Message = "已移除课程的旧提醒"
        });
        store.Save();

        var preview = new CourseImportPreview();
        preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Duplicate, new CourseItem
        {
            Id = existing.Id,
            Name = existing.Name,
            DayOfWeek = existing.DayOfWeek,
            StartTime = existing.StartTime,
            EndTime = existing.EndTime
        }, existing.Name));
        preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Add, new CourseItem
        {
            Name = "新课程",
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(10, 0),
            EndTime = new TimeOnly(11, 40)
        }, "新课程"));

        var importer = new CourseScheduleImporter(store);
        importer.Apply(preview, replaceCurrentSemester: true);
        Assert(store.Data.Courses.Count == 2
            && store.Data.Courses.Any(course => course.Id == existing.Id)
            && store.Data.Courses.Any(course => course.Name == "新课程")
            && store.Data.Courses.All(course => course.Name != "文件未包含的课"),
            "replacement should retain duplicate import rows and drop only omitted courses");
        Assert(store.Data.PendingAlerts.All(alert => alert.SourceId != omitted.Id),
            "replacement should remove pending alerts for omitted courses");

        var duplicatesOnly = new CourseImportPreview();
        duplicatesOnly.Rows.Add(preview.Rows[0]);
        importer.Apply(duplicatesOnly, replaceCurrentSemester: true);
        Assert(store.Data.Courses.Count == 1 && store.Data.Courses[0].Id == existing.Id,
            "a duplicate-only file should still be a valid replacement");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("course import merge preserves local skipped dates and alert history", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        var store = new ProductivityStore(directory);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var existing = new CourseItem
        {
            Name = "待更新课程",
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(9, 40),
            Teacher = "旧老师",
            ExcludedDates = [today],
            LastAlertedDate = today
        };
        store.Data.Courses.Add(existing);
        var preview = new CourseImportPreview();
        preview.Rows.Add(new CourseImportRow(CourseImportDisposition.Update, new CourseItem
        {
            Id = existing.Id,
            Name = existing.Name,
            DayOfWeek = existing.DayOfWeek,
            StartTime = existing.StartTime,
            EndTime = existing.EndTime,
            Teacher = "新老师"
        }, existing.Name));

        new CourseScheduleImporter(store).Apply(preview, replaceCurrentSemester: false);

        var updated = new ProductivityStore(directory).Data.Courses.Single();
        Assert(updated.Teacher == "新老师" && updated.ExcludedDates.Contains(today)
            && updated.LastAlertedDate == today,
            "merge should apply imported fields without erasing local skip dates or alert history");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("ICS recurrence and exclusion dates become local course dates", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var ics = Path.Combine(directory, "courses.ics");
        File.WriteAllText(ics, "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//DragonDeskPet Test//CN\r\nBEGIN:VEVENT\r\nUID:math-test\r\nDTSTART:20260907T080000\r\nDTEND:20260907T094000\r\nRRULE:FREQ=WEEKLY;COUNT=3\r\nEXDATE:20260914T080000\r\nSUMMARY:高等数学\r\nLOCATION:A101\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", new UTF8Encoding(false));
        var store = new ProductivityStore(directory);
        store.Data.Semester.StartDate = new DateOnly(2026, 9, 7);
        var importer = new CourseScheduleImporter(store);
        var preview = importer.Preview(ics, store.Data.Courses, store.Data.Semester);
        Assert(preview.AddedCount == 1, "recurring ICS event should preview as a course");
        var course = preview.Rows.Single(row => row.Course is not null).Course!;
        Assert(course.IncludedDates.SetEquals([new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 21)]), "EXDATE should remove the excluded recurrence");
        Assert(course.StartTime == new TimeOnly(8, 0), "floating ICS time should remain local");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    return Task.CompletedTask;
});

await RunAsync("corrupt productivity data is backed up before clean recovery", () =>
{
    var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "productivity.json");
        File.WriteAllText(path, "{not-json");
        var store = new ProductivityStore(directory);
        Assert(store.Data.Reminders.Count == 0, "corrupt data should recover to a blank store");
        Assert(!string.IsNullOrWhiteSpace(store.RecoveryNotice), "corrupt recovery should provide a user-facing notice");
        Assert(Directory.GetFiles(directory, "*.corrupt").Length == 1, "corrupt original should be retained as a timestamped backup");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
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
