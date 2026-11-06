using System.Diagnostics;

namespace ProfileOnPC.Models;

/// <summary>Вариант приоритета процесса для выпадающего списка.</summary>
public sealed record PriorityOption(string Value, string Label);

/// <summary>Допустимые приоритеты CPU для процессов профиля.</summary>
public static class ProcessPriorities
{
    public const string Idle = "idle";
    public const string BelowNormal = "below";
    public const string Normal = "normal";
    public const string AboveNormal = "above";
    public const string High = "high";

    public static IReadOnlyList<PriorityOption> All { get; } = new[]
    {
        new PriorityOption(Idle, "Простой"),
        new PriorityOption(BelowNormal, "Ниже обычного"),
        new PriorityOption(Normal, "Обычный"),
        new PriorityOption(AboveNormal, "Выше обычного"),
        new PriorityOption(High, "Высокий")
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Normal;

        var match = All.FirstOrDefault(option =>
            string.Equals(option.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Value ?? Normal;
    }

    public static string GetLabel(string? value)
    {
        var normalized = Normalize(value);
        return All.FirstOrDefault(option => option.Value == normalized)?.Label ?? normalized;
    }

    public static ProcessPriorityClass ToPriorityClass(string? value) => Normalize(value) switch
    {
        Idle => ProcessPriorityClass.Idle,
        BelowNormal => ProcessPriorityClass.BelowNormal,
        AboveNormal => ProcessPriorityClass.AboveNormal,
        High => ProcessPriorityClass.High,
        _ => ProcessPriorityClass.Normal
    };
}
