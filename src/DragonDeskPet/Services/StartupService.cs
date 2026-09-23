using Microsoft.Win32;

namespace DragonDeskPet.Services;

public static class StartupService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DragonDeskPet";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            key.SetValue(ValueName, $"\"{executable}\"");
        }
    }
}
