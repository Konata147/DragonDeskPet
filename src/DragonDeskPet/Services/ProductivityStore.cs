using System.IO;
using System.Text.Json;
using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class ProductivityStore : IProductivityStore
{
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProductivityStore(string dataDirectory)
    {
        DataPath = Path.Combine(dataDirectory, "productivity.json");
        Data = Load();
        NormalizeForToday(DateOnly.FromDateTime(DateTime.Today));
    }

    public ProductivityData Data { get; private set; }
    public string DataPath { get; }
    public string? RecoveryNotice { get; private set; }

    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            var temporaryPath = DataPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Data, _jsonOptions));
            File.Move(temporaryPath, DataPath, overwrite: true);
        }
    }

    public bool NormalizeForToday(DateOnly today)
    {
        var changed = false;
        var cutoffUtc = new DateTimeOffset(today.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(today.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        changed |= Data.Todos.RemoveAll(todo => todo.IsCompleted && todo.CompletedUtc is { } completed && completed < cutoffUtc) > 0;
        foreach (var todo in Data.Todos.Where(todo => !todo.IsCompleted && todo.AssignedDate < today))
        {
            todo.AssignedDate = today;
            changed = true;
        }

        return changed;
    }

    private ProductivityData Load()
    {
        if (!File.Exists(DataPath))
        {
            return new ProductivityData();
        }

        try
        {
            var data = JsonSerializer.Deserialize<ProductivityData>(File.ReadAllText(DataPath), _jsonOptions)
                ?? new ProductivityData();
            data.Reminders ??= [];
            data.PendingAlerts ??= [];
            data.Todos ??= [];
            data.Courses ??= [];
            data.Semester ??= new SemesterSettings();
            data.Pomodoro ??= new PomodoroState();
            foreach (var course in data.Courses)
            {
                course.ExcludedDates ??= [];
                course.IncludedDates ??= [];
            }

            data.SchemaVersion = 1;
            return data;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            try
            {
                var backupPath = $"{DataPath}.{DateTime.Now:yyyyMMdd-HHmmss}.corrupt";
                File.Move(DataPath, backupPath, overwrite: true);
                RecoveryNotice = $"效率数据已损坏，原文件已保留为：{backupPath}";
            }
            catch
            {
                RecoveryNotice = "效率数据无法读取，已使用空白数据启动；原文件未被覆盖。";
            }

            return new ProductivityData();
        }
    }
}
