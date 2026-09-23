using DragonDeskPet.AI;
using DragonDeskPet.Core;
using DragonDeskPet.Services;
using System.Diagnostics;
using System.Drawing;

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

    return Task.CompletedTask;
});

await RunAsync("offline provider gives a useful local response", async () =>
{
    var provider = new AiProviderFactory().Create(new AppSettings());
    var response = await provider.SendAsync("hello");
    Assert(!provider.IsConfigured, "offline provider must not report configured");
    Assert(response.Contains("设置", StringComparison.Ordinal), "offline response should direct the user to settings");
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
