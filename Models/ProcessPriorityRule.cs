using System.Text.Json.Serialization;

namespace ProfileOnPC.Models;

/// <summary>Правило «процесс → приоритет CPU», применяемое при активации профиля.</summary>
public sealed class ProcessPriorityRule
{
    [JsonPropertyName("process")]
    public string ProcessName { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = ProcessPriorities.Normal;

    public ProcessPriorityRule Clone() => new()
    {
        ProcessName = ProcessName,
        Priority = Priority
    };

    public override string ToString() => $"{ProcessName} → {ProcessPriorities.GetLabel(Priority)}";
}
