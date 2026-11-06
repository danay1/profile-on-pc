using System.Diagnostics;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>Что инициировало активацию профиля.</summary>
public enum ActivationSource
{
    User,
    Hotkey,
    Trigger,
    Tray,
    Startup
}

/// <summary>Итог активации профиля.</summary>
public sealed record ActivationReport(
    ProfileModel Profile,
    ActivationSource Source,
    IReadOnlyList<ActionResult> Results,
    TimeSpan Duration)
{
    public int CompletedCount => Results.Count(result => result.Success && !result.Skipped);

    public int SkippedCount => Results.Count(result => result.Skipped);

    public int ErrorCount => Results.Count(result => !result.Success);

    public IReadOnlyList<ActionResult> Errors =>
        Results.Where(result => !result.Success).ToArray();
}

/// <summary>
/// Выполняет весь сценарий активации профиля в фоне: оптимизация, приоритеты,
/// окна, закрытие/запуск процессов и открытие ссылок.
/// </summary>
public sealed class ProfileActivationService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly OptimizationService _optimization;
    private readonly ProcessService _processes;
    private readonly ProcessPriorityService _priorities;
    private readonly WindowService _windows;
    private readonly SystemStateService _state;

    private readonly object _windowsLock = new();
    private readonly List<CapturedWindow> _hiddenWindows = [];

    private bool _disposed;

    public ProfileActivationService(
        OptimizationService optimization,
        ProcessService processes,
        ProcessPriorityService priorities,
        WindowService windows,
        SystemStateService state)
    {
        _optimization = optimization;
        _processes = processes;
        _priorities = priorities;
        _windows = windows;
        _state = state;
    }

    /// <summary>Состояние системы до последней активации — для кнопки «Вернуть как было».</summary>
    public SystemStateSnapshot? LastSnapshot { get; private set; }

    /// <summary>Есть что откатывать.</summary>
    public bool CanRestore => LastSnapshot is not null && !LastSnapshot.IsEmpty;

    /// <summary>Текущий шаг активации (для строки состояния).</summary>
    public event Action<string>? ProgressChanged;

    /// <summary>Активация завершилась (успешно или с ошибками).</summary>
    public event Action<ActivationReport>? Completed;

    public bool IsBusy => _gate.CurrentCount == 0;

    public bool HasHiddenWindows
    {
        get
        {
            lock (_windowsLock)
                return _hiddenWindows.Count > 0;
        }
    }

    /// <summary>
    /// Активирует профиль. Возвращает null, если другая активация уже выполняется.
    /// </summary>
    public async Task<ActivationReport?> ActivateAsync(
        ProfileModel profile,
        ActivationSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return null;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var results = await Task
                .Run(() => RunStepsAsync(profile, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            var report = new ActivationReport(profile, source, results, stopwatch.Elapsed);
            Completed?.Invoke(report);
            return report;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var report = new ActivationReport(
                profile,
                source,
                [ActionResult.Fail($"Сбой активации: {exception.Message}")],
                stopwatch.Elapsed);

            Completed?.Invoke(report);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Возвращает в обычное состояние окна, скрытые предыдущей активацией.</summary>
    public int RestoreHiddenWindows()
    {
        CapturedWindow[] windows;

        lock (_windowsLock)
        {
            if (_hiddenWindows.Count == 0)
                return 0;

            windows = [.. _hiddenWindows];
            _hiddenWindows.Clear();
        }

        return _windows.Restore(windows);
    }

    /// <summary>
    /// Возвращает систему в состояние до последней активации: схема питания, SysMain,
    /// Game DVR, анимации и скрытые окна.
    /// </summary>
    public async Task<IReadOnlyList<ActionResult>> RestoreLastStateAsync(
        CancellationToken cancellationToken = default)
    {
        var snapshot = LastSnapshot;
        var results = new List<ActionResult>();

        if (snapshot is null || snapshot.IsEmpty)
        {
            results.Add(ActionResult.Skip("Нет сохранённого состояния для отката"));
            return results;
        }

        results.AddRange(await _state.RestoreAsync(snapshot, cancellationToken).ConfigureAwait(false));

        var restoredWindows = RestoreHiddenWindows();
        results.Add(restoredWindows > 0
            ? ActionResult.Ok($"Возвращено окон: {restoredWindows}")
            : ActionResult.Skip("Скрытых окон не было"));

        LastSnapshot = null;
        AppLog.Info($"Откат активации: снимок от {snapshot.CapturedAt:HH:mm:ss} восстановлен");
        return results;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        RestoreHiddenWindows();
    }

    private async Task<IReadOnlyList<ActionResult>> RunStepsAsync(
        ProfileModel profile,
        CancellationToken cancellationToken)
    {
        var results = new List<ActionResult>();

        void Report(string message) => ProgressChanged?.Invoke(message);

        // Окна, скрытые в прошлый раз, нужно вернуть до новой активации.
        RestoreHiddenWindows();

        // Запоминаем состояние системы, если профиль собирается его менять:
        // это позволяет откатить активацию кнопкой «Вернуть как было».
        var options = profile.Optimizations ?? new OptimizationOptions();
        options.Normalize();

        var changesSystemState = options.PowerPlan != PowerPlans.Keep
                                 || options.SysMain != SysMainModes.Keep
                                 || options.DisableGameDvr
                                 || options.DisableAnimations;

        if (changesSystemState)
        {
            Report("Запоминаю текущее состояние системы…");

            try
            {
                LastSnapshot = await _state.CaptureAsync(cancellationToken).ConfigureAwait(false);
                AppLog.Info($"Снимок состояния до активации «{profile.Name}»: {LastSnapshot}");
            }
            catch (Exception exception)
            {
                AppLog.Warn("Не удалось запомнить состояние системы", exception);
            }
        }

        Report("Очистка и оптимизация…");
        results.AddRange(await _optimization
            .ApplyAsync(options, Report, cancellationToken)
            .ConfigureAwait(false));

        foreach (var rule in profile.PriorityRules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(rule.ProcessName))
                continue;

            Report($"Приоритет {rule.ProcessName}.exe: {ProcessPriorities.GetLabel(rule.Priority)}");
            results.Add(_priorities.Apply(rule.ProcessName, rule.Priority));
        }

        var windowAction = WindowActions.Normalize(profile.Optimizations?.BackgroundWindowAction);
        if (windowAction != WindowActions.None)
        {
            Report("Сворачивание фоновых приложений…");
            results.Add(ApplyWindowAction(profile, windowAction));
        }

        foreach (var process in profile.ProcessesToClose)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report($"Закрытие: {process}");
            results.Add(await _processes.CloseAsync(process).ConfigureAwait(false));
        }

        foreach (var process in profile.ProcessesToStart)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report($"Запуск: {process}");
            results.Add(await _processes.StartAsync(process).ConfigureAwait(false));
        }

        foreach (var url in profile.UrlsToOpen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report($"Открытие: {url}");
            results.Add(await _processes.OpenUrlAsync(url).ConfigureAwait(false));
        }

        return results;
    }

    private ActionResult ApplyWindowAction(ProfileModel profile, string windowAction)
    {
        // Приложения из «Запустить при активации» остаются на рабочем столе.
        var windows = _windows.CaptureBackgroundWindows(profile.ProcessesToStart);
        if (windows.Count == 0)
            return ActionResult.Skip("Фоновые окна не найдены");

        if (windowAction == WindowActions.Minimize)
        {
            var minimized = _windows.Minimize(windows);
            return minimized > 0
                ? ActionResult.Ok($"Свернуто на панель задач окон: {minimized}")
                : ActionResult.Fail("Не удалось свернуть фоновые окна");
        }

        var hidden = _windows.Hide(windows);

        lock (_windowsLock)
            _hiddenWindows.AddRange(hidden);

        return hidden.Count > 0
            ? ActionResult.Ok($"Скрыто окон: {hidden.Count} — вернуть можно из меню трея")
            : ActionResult.Fail("Не удалось скрыть фоновые окна");
    }
}
