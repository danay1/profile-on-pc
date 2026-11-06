using System.Text.Json.Serialization;

namespace ProfileOnPC.Models;

/// <summary>Один шаг активации в истории.</summary>
public sealed class ActivationStepRecord
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("skipped")]
    public bool Skipped { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Запись истории активации профиля: что сделали, чем закончилось и в каком контексте.</summary>
public sealed class ActivationHistoryEntry
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonPropertyName("profile_id")]
    public string ProfileId { get; set; } = string.Empty;

    [JsonPropertyName("profile_name")]
    public string ProfileName { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("duration_ms")]
    public double DurationMs { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;

    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("errors")]
    public int Errors { get; set; }

    [JsonPropertyName("steps")]
    public List<ActivationStepRecord> Steps { get; set; } = [];

    /// <summary>Заголовок строки в списке истории.</summary>
    [JsonIgnore]
    public string Title => $"{Timestamp:dd.MM HH:mm} · {ProfileName} · {Source}";

    /// <summary>Краткая сводка результата.</summary>
    [JsonIgnore]
    public string Summary =>
        Errors == 0
            ? $"выполнено {Completed}, пропущено {Skipped}, {DurationMs / 1000:0.0} с"
            : $"выполнено {Completed}, ошибок {Errors}, {DurationMs / 1000:0.0} с";

    /// <summary>Подробный отчёт для панели справа.</summary>
    [JsonIgnore]
    public string Details
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"Профиль: {ProfileName}");
            builder.AppendLine($"Источник: {Source}");
            builder.AppendLine($"Время: {Timestamp:dd.MM.yyyy HH:mm:ss}");
            builder.AppendLine($"Длительность: {DurationMs:0} мс");
            builder.AppendLine($"Контекст: {Context}");
            builder.AppendLine();

            foreach (var step in Steps)
            {
                var marker = !step.Success ? "✕" : step.Skipped ? "·" : "•";
                builder.AppendLine($"{marker} {step.Message}");
            }

            return builder.ToString();
        }
    }
}
