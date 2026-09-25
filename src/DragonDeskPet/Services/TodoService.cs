using DragonDeskPet.Core;

namespace DragonDeskPet.Services;

public sealed class TodoService
{
    private readonly IProductivityStore _store;

    public TodoService(IProductivityStore store) => _store = store;

    public IReadOnlyList<TodoItem> GetForDate(DateOnly date) => _store.Data.Todos
        .Where(item => item.AssignedDate == date)
        .OrderBy(item => item.IsCompleted)
        .ThenBy(item => item.CreatedUtc)
        .ToList();

    public TodoItem Add(string text, DateOnly date)
    {
        var item = new TodoItem { Text = text.Trim(), AssignedDate = date };
        _store.Data.Todos.Add(item);
        _store.Save();
        return item;
    }

    public void Update(Guid id, string text)
    {
        var item = _store.Data.Todos.FirstOrDefault(candidate => candidate.Id == id);
        if (item is null)
        {
            return;
        }

        item.Text = text.Trim();
        _store.Save();
    }

    public void SetCompleted(Guid id, bool completed, DateTimeOffset nowUtc)
    {
        var item = _store.Data.Todos.FirstOrDefault(candidate => candidate.Id == id);
        if (item is null)
        {
            return;
        }

        item.IsCompleted = completed;
        item.CompletedUtc = completed ? nowUtc.ToUniversalTime() : null;
        _store.Save();
    }

    public void Delete(Guid id)
    {
        _store.Data.Todos.RemoveAll(item => item.Id == id);
        _store.Save();
    }

    public void Maintain(DateOnly today)
    {
        if (_store is ProductivityStore store)
        {
            if (store.NormalizeForToday(today))
            {
                store.Save();
            }
        }
    }
}
