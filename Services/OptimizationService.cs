using Microsoft.Win32;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>
/// Оркестратор «Очистки и оптимизации»: выполняет выбранные действия по порядку
/// и возвращает по одному результату на каждое действие.
/// </summary>
public sealed class OptimizationService
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(20);

    private readonly SystemServiceManager _services = new();
    private readonly CleanupService _cleanup;

    public OptimizationService()
    {
        _cleanup = new CleanupService(_services);
        Power = new PowerPlanService();
    }

    /// <summary>Доступ к powercfg — используется интерфейсом для показа активной схемы.</summary>
    public PowerPlanService Power { get; }

    public async Task<IReadOnlyList<ActionResult>> ApplyAsync(
        OptimizationOptions? options,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = (options ?? new OptimizationOptions()).Clone();
        settings.Normalize();

        var results = new List<ActionResult>();
        void Report(string message) => progress?.Invoke(message);

        cancellationToken.ThrowIfCancellationRequested();

        if (settings.ClearTempFiles)
        {
            Report("Очистка временных файлов…");
            results.Add(_cleanup.ClearTempFiles());
        }

        if (settings.ClearBrowserCache)
        {
            Report("Очистка кеша браузеров…");
            results.Add(_cleanup.ClearBrowserCache());
        }

        if (settings.EmptyRecycleBin)
        {
            Report("Очистка корзины…");
            results.Add(_cleanup.EmptyRecycleBin());
        }

        if (settings.FlushDns)
        {
            Report("Сброс кэша DNS…");
            results.Add(await _cleanup.FlushDnsAsync(cancellationToken).ConfigureAwait(false));
        }

        if (settings.ClearPrefetch)
        {
            Report("Очистка Prefetch…");
            results.Add(_cleanup.ClearPrefetch());
        }

        if (settings.ClearSoftwareDistribution)
        {
            Report("Очистка SoftwareDistribution…");
            results.Add(await _cleanup
                .ClearSoftwareDistributionAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        if (settings.ClearStandbyList)
        {
            Report("Очистка Standby List (RAM)…");
            results.Add(_cleanup.ClearStandbyList());
        }

        if (settings.PowerPlan != PowerPlans.Keep)
        {
            Report($"Схема питания: {PowerPlans.GetLabel(settings.PowerPlan)}…");
            results.Add(await Power.ApplyAsync(settings.PowerPlan, cancellationToken).ConfigureAwait(false));
        }

        if (settings.SysMain != SysMainModes.Keep)
        {
            Report("Настройка службы SysMain…");
            results.Add(await ConfigureSysMainAsync(settings.SysMain, cancellationToken).ConfigureAwait(false));
        }

        if (settings.DisableGameDvr)
        {
            Report("Отключение Game DVR…");
            results.Add(DisableGameDvr());
        }

        if (settings.DisableAnimations)
        {
            Report("Отключение анимаций…");
            results.Add(DisableAnimations());
        }

        return results;
    }

    private async Task<ActionResult> ConfigureSysMainAsync(string mode, CancellationToken cancellationToken)
    {
        var normalized = SysMainModes.Normalize(mode);
        if (normalized == SysMainModes.Keep)
            return ActionResult.Skip("Служба SysMain не изменялась");

        if (!ElevationService.IsAdministrator)
            return ActionResult.Fail("Управление службой SysMain требует прав администратора");

        const string service = SysMainModes.ServiceName;
        var stepResults = new List<ActionResult>();

        if (normalized == SysMainModes.Disable)
        {
            // Порядок важен: пока служба работает, SCM может отказать в смене типа запуска.
            stepResults.Add(await _services
                .StopAsync(service, ServiceTimeout, cancellationToken)
                .ConfigureAwait(false));
            stepResults.Add(_services.SetStartup(service, automatic: false));
        }
        else
        {
            stepResults.Add(_services.SetStartup(service, automatic: true));
            stepResults.Add(await _services
                .StartAsync(service, ServiceTimeout, cancellationToken)
                .ConfigureAwait(false));
        }

        return Combine(stepResults);
    }

    private static ActionResult DisableGameDvr()
    {
        try
        {
            using var gameConfig = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", true);
            gameConfig.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);

            using var capture = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\GameDVR",
                true);
            capture.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);

            return ActionResult.Ok("Фоновая запись Game DVR отключена");
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"Не удалось отключить Game DVR: {exception.Message}");
        }
    }

    private static ActionResult DisableAnimations()
    {
        try
        {
            using var desktop = Registry.CurrentUser.OpenSubKey(
                @"Control Panel\Desktop",
                writable: true);
            desktop?.SetValue("MinAnimate", "0", RegistryValueKind.String);

            using var visualEffects = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                true);
            visualEffects.SetValue("VisualFXSetting", 2, RegistryValueKind.DWord);

            return ActionResult.Ok("Анимации отключены; полностью применятся после входа в Windows");
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"Не удалось изменить анимации: {exception.Message}");
        }
    }

    private static ActionResult Combine(IReadOnlyList<ActionResult> results)
    {
        var messages = results
            .Where(result => !result.Skipped)
            .Select(result => result.Message)
            .ToArray();

        var message = messages.Length > 0 ? string.Join("; ", messages) : "Без изменений";

        if (results.Any(result => !result.Success))
            return ActionResult.Fail(message);

        return results.All(result => result.Skipped)
            ? ActionResult.Skip(message)
            : ActionResult.Ok(message);
    }
}
