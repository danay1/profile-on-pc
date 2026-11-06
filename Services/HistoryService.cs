using System.Text.Json;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>История активаций профилей (последние <see cref="MaxEntries"/> записей).</summary>
public sealed class HistoryService
{
    public const int MaxEntries = 100;

    private readonly object _sync = new();
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string FilePath { get; } = Path.Combine(DataDirectory, "history.json");

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProfileOnPC");

    public IReadOnlyList<ActivationHistoryEntry> Load()
    {
        lock (_sync)
            return LoadUnsafe();
    }

    public async Task<SaveResult> AddAsync(ActivationHistoryEntry entry)
    {
        List<ActivationHistoryEntry> entries;

        lock (_sync)
        {
            entries = [.. LoadUnsafe()];
            entries.Insert(0, entry);

            if (entries.Count > MaxEntries)
                entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        var json = JsonSerializer.Serialize(entries, _options);
        return await AtomicFile.WriteAsync(FilePath, json).ConfigureAwait(false);
    }

    public void Clear()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(FilePath))
                    File.Delete(FilePath);
            }
            catch (Exception exception)
            {
                AppLog.Warn("Не удалось очистить историю", exception);
            }
        }
    }

    private List<ActivationHistoryEntry> LoadUnsafe()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ActivationHistoryEntry>>(json, _options) ?? [];
        }
        catch (Exception exception)
        {
            AppLog.Warn("Не удалось прочитать историю активаций", exception);
            return [];
        }
    }
}
