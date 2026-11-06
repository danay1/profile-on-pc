using System.Text.Json.Serialization;

namespace ProfileOnPC.Models;

/// <summary>Глобальные настройки приложения.</summary>
public sealed class AppSettings
{
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "Dark";

    [JsonPropertyName("accent_color")]
    public string AccentColor { get; set; } = "#2563A6";

    [JsonPropertyName("autostart")]
    public bool StartWithWindows { get; set; }

    /// <summary>Устаревший флаг: сворачивать ли в трей. Оставлен для чтения старых файлов.</summary>
    [JsonPropertyName("minimize_to_tray")]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Что делать при закрытии окна: tray | minimize | exit.</summary>
    [JsonPropertyName("close_action")]
    public string CloseAction { get; set; } = CloseActions.Tray;

    /// <summary>Глобальные горячие клавиши для быстрой смены профилей.</summary>
    [JsonPropertyName("global_hotkeys")]
    public bool GlobalHotkeysEnabled { get; set; } = true;

    /// <summary>Авто-активация профиля при запуске отслеживаемых приложений.</summary>
    [JsonPropertyName("process_triggers")]
    public bool ProcessTriggersEnabled { get; set; } = true;

    /// <summary>Toast-уведомление в трее после активации профиля.</summary>
    [JsonPropertyName("activation_toast")]
    public bool ActivationToastEnabled { get; set; } = true;

    /// <summary>Профиль, активированный последним — отображается как активный.</summary>
    [JsonPropertyName("active_profile_id")]
    public string ActiveProfileId { get; set; } = string.Empty;

    /// <summary>Применять сохранённый активный профиль при старте приложения.</summary>
    [JsonPropertyName("apply_profile_on_startup")]
    public bool ApplyProfileOnStartup { get; set; }

    public void Normalize()
    {
        Theme = string.Equals(Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";

        if (string.IsNullOrWhiteSpace(CloseAction))
            CloseAction = MinimizeToTray ? CloseActions.Tray : CloseActions.Exit;

        CloseAction = Models.CloseActions.Normalize(CloseAction);
        ActiveProfileId ??= string.Empty;
    }

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        AccentColor = AccentColor,
        StartWithWindows = StartWithWindows,
        MinimizeToTray = MinimizeToTray,
        CloseAction = CloseAction,
        GlobalHotkeysEnabled = GlobalHotkeysEnabled,
        ProcessTriggersEnabled = ProcessTriggersEnabled,
        ActivationToastEnabled = ActivationToastEnabled,
        ActiveProfileId = ActiveProfileId,
        ApplyProfileOnStartup = ApplyProfileOnStartup
    };
}

/// <summary>Действие при закрытии окна приложения.</summary>
public static class CloseActions
{
    public const string Tray = "tray";
    public const string Minimize = "minimize";
    public const string Exit = "exit";

    public static IReadOnlyList<ChoiceOption> All { get; } = new[]
    {
        new ChoiceOption(Tray, "Сворачивать в трей"),
        new ChoiceOption(Minimize, "Сворачивать на панель задач"),
        new ChoiceOption(Exit, "Закрывать приложение")
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Tray;

        var match = All.FirstOrDefault(option =>
            string.Equals(option.Value, value.Trim(), StringComparison.OrdinalIgnoreCase));

        return match?.Value ?? Tray;
    }

    public static string GetLabel(string? value)
    {
        var normalized = Normalize(value);
        return All.FirstOrDefault(option => option.Value == normalized)?.Label ?? normalized;
    }
}
