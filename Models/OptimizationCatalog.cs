namespace ProfileOnPC.Models;

/// <summary>Вариант выбора для выпадающего списка.</summary>
public sealed record ChoiceOption(string Value, string Label);

/// <summary>Вариант схемы питания.</summary>
public sealed record PowerPlanOption(string Value, string Label);

/// <summary>Схемы питания Windows и их GUID.</summary>
public static class PowerPlans
{
    public const string Keep = "keep";
    public const string Balanced = "balanced";
    public const string HighPerformance = "high";
    public const string Ultimate = "ultimate";

    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    /// <summary>Скрытая схема «Ultimate Performance»; на части систем создаётся через /duplicatescheme.</summary>
    public const string UltimateGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public static IReadOnlyList<PowerPlanOption> All { get; } = new[]
    {
        new PowerPlanOption(Keep, "Не изменять"),
        new PowerPlanOption(Balanced, "Сбалансированная"),
        new PowerPlanOption(HighPerformance, "Высокая производительность"),
        new PowerPlanOption(Ultimate, "Ultimate Performance")
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Keep;

        var match = All.FirstOrDefault(option =>
            string.Equals(option.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Value ?? Keep;
    }

    public static string GetLabel(string? value)
    {
        var normalized = Normalize(value);
        return All.FirstOrDefault(option => option.Value == normalized)?.Label ?? normalized;
    }

    /// <summary>GUID схемы по внутреннему идентификатору. Для <see cref="Keep"/> возвращается null.</summary>
    public static string? GetGuid(string? value) => Normalize(value) switch
    {
        Balanced => BalancedGuid,
        HighPerformance => HighPerformanceGuid,
        Ultimate => UltimateGuid,
        _ => null
    };
}

/// <summary>Режим работы службы SysMain (Superfetch).</summary>
public static class SysMainModes
{
    public const string Keep = "keep";
    public const string Enable = "enable";
    public const string Disable = "disable";

    public const string ServiceName = "SysMain";

    public static IReadOnlyList<ChoiceOption> All { get; } = new[]
    {
        new ChoiceOption(Keep, "Не изменять"),
        new ChoiceOption(Enable, "Включить (как в Windows по умолчанию)"),
        new ChoiceOption(Disable, "Отключить и запретить запуск")
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Keep;

        var match = All.FirstOrDefault(option =>
            string.Equals(option.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Value ?? Keep;
    }

    public static string GetLabel(string? value)
    {
        var normalized = Normalize(value);
        return All.FirstOrDefault(option => option.Value == normalized)?.Label ?? normalized;
    }
}

/// <summary>Что делать с окнами фоновых приложений при активации профиля.</summary>
public static class WindowActions
{
    public const string None = "none";
    public const string Minimize = "minimize";
    public const string HideToTray = "hide";

    public static IReadOnlyList<ChoiceOption> All { get; } = new[]
    {
        new ChoiceOption(None, "Не изменять"),
        new ChoiceOption(Minimize, "Свернуть окна на панель задач"),
        new ChoiceOption(HideToTray, "Скрыть окна в трей ProfileOnPC")
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return None;

        var match = All.FirstOrDefault(option =>
            string.Equals(option.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Value ?? None;
    }

    public static string GetLabel(string? value)
    {
        var normalized = Normalize(value);
        return All.FirstOrDefault(option => option.Value == normalized)?.Label ?? normalized;
    }
}
