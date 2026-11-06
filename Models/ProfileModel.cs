using System.Text.Json.Serialization;

namespace ProfileOnPC.Models;

/// <summary>Профиль рабочего стола: что закрыть/запустить, открыть и как оптимизировать систему.</summary>
public sealed class ProfileModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Новый профиль";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("processes_to_close")]
    public List<string> ProcessesToClose { get; set; } = [];

    [JsonPropertyName("processes_to_start")]
    public List<string> ProcessesToStart { get; set; } = [];

    [JsonPropertyName("urls_to_open")]
    public List<string> UrlsToOpen { get; set; } = [];

    /// <summary>Приоритеты CPU для процессов, применяемые при активации.</summary>
    [JsonPropertyName("priority_rules")]
    public List<ProcessPriorityRule> PriorityRules { get; set; } = [];

    /// <summary>Имена процессов, запуск которых автоматически активирует профиль.</summary>
    [JsonPropertyName("trigger_processes")]
    public List<string> TriggerProcesses { get; set; } = [];

    /// <summary>Включена ли авто-активация по появлению процессов из <see cref="TriggerProcesses"/>.</summary>
    [JsonPropertyName("auto_activate_on_trigger")]
    public bool AutoActivateOnTrigger { get; set; }

    /// <summary>Глобальная горячая клавиша профиля, например «Ctrl+Alt+1».</summary>
    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = string.Empty;

    /// <summary>Показывать уведомление в трее после успешной активации.</summary>
    [JsonPropertyName("notify_on_activation")]
    public bool NotifyOnActivation { get; set; } = true;

    [JsonPropertyName("optimizations")]
    public OptimizationOptions Optimizations { get; set; } = new();

    /// <summary>Приводит профиль к согласованному состоянию после чтения из файла и перед сохранением.</summary>
    public void Normalize()
    {
        ProcessesToClose ??= [];
        ProcessesToStart ??= [];
        UrlsToOpen ??= [];
        PriorityRules ??= [];
        TriggerProcesses ??= [];
        Optimizations ??= new OptimizationOptions();

        Name = string.IsNullOrWhiteSpace(Name) ? "Новый профиль" : Name.Trim();
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id;
        Description ??= string.Empty;
        Hotkey = Hotkey?.Trim() ?? string.Empty;

        ProcessesToClose = ProcessesToClose
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ProcessesToStart = ProcessesToStart
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        UrlsToOpen = UrlsToOpen
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TriggerProcesses = TriggerProcesses
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        PriorityRules = PriorityRules
            .Where(rule => rule is not null && !string.IsNullOrWhiteSpace(rule.ProcessName))
            .ToList();

        foreach (var rule in PriorityRules)
        {
            rule.ProcessName = rule.ProcessName.Trim();
            rule.Priority = ProcessPriorities.Normalize(rule.Priority);
        }

        Optimizations.Normalize();
    }

    public override string ToString() => Name;
}
