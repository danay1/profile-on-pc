using System.Text.Json.Serialization;

namespace ProfileOnPC.Models;

/// <summary>
/// Действия «Очистка и оптимизация», которые выполняются при активации профиля.
/// </summary>
public sealed class OptimizationOptions
{
    [JsonPropertyName("clear_temp")]
    public bool ClearTempFiles { get; set; }

    [JsonPropertyName("clear_browser_cache")]
    public bool ClearBrowserCache { get; set; }

    [JsonPropertyName("cleanup_recycle")]
    public bool EmptyRecycleBin { get; set; }

    /// <summary>Сброс кэша DNS (ipconfig /flushdns).</summary>
    [JsonPropertyName("flush_dns")]
    public bool FlushDns { get; set; }

    /// <summary>Удаление файлов предзагрузки %SystemRoot%\Prefetch\*.pf.</summary>
    [JsonPropertyName("clear_prefetch")]
    public bool ClearPrefetch { get; set; }

    /// <summary>Очистка кэша обновлений %SystemRoot%\SoftwareDistribution\Download.</summary>
    [JsonPropertyName("clear_software_distribution")]
    public bool ClearSoftwareDistribution { get; set; }

    /// <summary>Сброс списка ожидания (Standby List) через NtSetSystemInformation.</summary>
    [JsonPropertyName("clear_standby_list")]
    public bool ClearStandbyList { get; set; }

    [JsonPropertyName("disable_game_dvr")]
    public bool DisableGameDvr { get; set; }

    [JsonPropertyName("disable_animations")]
    public bool DisableAnimations { get; set; }

    /// <summary>Схема питания: keep | balanced | high | ultimate.</summary>
    [JsonPropertyName("power_plan")]
    public string PowerPlan { get; set; } = PowerPlans.Keep;

    /// <summary>Служба SysMain: keep | enable | disable.</summary>
    [JsonPropertyName("sysmain")]
    public string SysMain { get; set; } = SysMainModes.Keep;

    /// <summary>Действие с окнами фоновых приложений: none | minimize | hide.</summary>
    [JsonPropertyName("background_windows")]
    public string BackgroundWindowAction { get; set; } = WindowActions.None;

    /// <summary>
    /// Устаревший флаг из версий до выбора схемы питания. Читается только для миграции
    /// и больше не записывается в файл (см. <see cref="Normalize"/>).
    /// </summary>
    [JsonPropertyName("high_performance_power")]
    public bool? LegacyHighPerformancePowerPlan { get; set; }

    /// <summary>Приводит значения к допустимым и переносит устаревшие настройки в новые поля.</summary>
    public void Normalize()
    {
        PowerPlan = PowerPlans.Normalize(PowerPlan);
        SysMain = SysMainModes.Normalize(SysMain);
        BackgroundWindowAction = WindowActions.Normalize(BackgroundWindowAction);

        if (LegacyHighPerformancePowerPlan == true && PowerPlan == PowerPlans.Keep)
            PowerPlan = PowerPlans.HighPerformance;

        LegacyHighPerformancePowerPlan = null;
    }

    public OptimizationOptions Clone() => new()
    {
        ClearTempFiles = ClearTempFiles,
        ClearBrowserCache = ClearBrowserCache,
        EmptyRecycleBin = EmptyRecycleBin,
        FlushDns = FlushDns,
        ClearPrefetch = ClearPrefetch,
        ClearSoftwareDistribution = ClearSoftwareDistribution,
        ClearStandbyList = ClearStandbyList,
        DisableGameDvr = DisableGameDvr,
        DisableAnimations = DisableAnimations,
        PowerPlan = PowerPlan,
        SysMain = SysMain,
        BackgroundWindowAction = BackgroundWindowAction
    };
}
