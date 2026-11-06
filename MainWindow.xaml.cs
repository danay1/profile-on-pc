using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ProfileOnPC.Helpers;
using ProfileOnPC.Models;
using ProfileOnPC.Services;

namespace ProfileOnPC;

public partial class MainWindow : Window
{
    private readonly StorageService _storage;
    private readonly ProcessService _processService = new();
    private readonly ProcessPriorityService _priorityService = new();
    private readonly WindowService _windowService = new();
    private readonly OptimizationService _optimizationService = new();
    private readonly ProfileActivationService _activationService;
    private readonly TrayService _trayService;
    private readonly HotkeyService _hotkeyService = new();
    private readonly ProcessTriggerService _triggerService = new();
    private readonly HistoryService _historyService = new();
    private readonly SystemStateService _stateService = new();

    private readonly ObservableCollection<ProfileModel> _profiles;
    private readonly ObservableCollection<string> _closeItems = [];
    private readonly ObservableCollection<string> _startItems = [];
    private readonly ObservableCollection<string> _urlItems = [];
    private readonly ObservableCollection<string> _triggerItems = [];
    private readonly ObservableCollection<ProcessPriorityRule> _priorityRules = [];
    private readonly ObservableCollection<ActivationHistoryEntry> _historyEntries = [];

    private ProfileModel? _selectedProfile;
    private AppSettings _settings;
    private bool _allowExit;
    private bool _changingSelection;
    private bool _isBusy;
    private bool _hotkeysReady;
    private bool _resourcesReleased;
    private bool _storageWarningShown;

    public MainWindow(StorageService storage, AppSettings settings)
    {
        InitializeComponent();

        _storage = storage;
        _settings = settings;

        _trayService = new TrayService();
        _activationService = new ProfileActivationService(
            _optimizationService,
            _processService,
            _priorityService,
            _windowService,
            _stateService);

        _profiles = new ObservableCollection<ProfileModel>(_storage.LoadProfiles());

        ProfileList.ItemsSource = _profiles;
        CloseList.ItemsSource = _closeItems;
        StartList.ItemsSource = _startItems;
        UrlList.ItemsSource = _urlItems;
        TriggerList.ItemsSource = _triggerItems;
        PriorityList.ItemsSource = _priorityRules;
        HistoryList.ItemsSource = _historyEntries;

        WireServices();

        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this, _settings.Theme);
        Loaded += MainWindow_OnLoaded;
    }

    // ------------------------------------------------------------ инициализация

    private void WireServices()
    {
        _activationService.ProgressChanged += message =>
            Dispatcher.InvokeAsync(() => SetStatus(message));

        _activationService.Completed += report =>
            Dispatcher.InvokeAsync(() => OnActivationCompleted(report));

        _trayService.ShowRequested += () => Dispatcher.InvokeAsync(RestoreFromTray);
        _trayService.ExitRequested += () => Dispatcher.InvokeAsync(ShutdownApplication);
        _trayService.RestoreWindowsRequested += () => Dispatcher.InvokeAsync(RestoreHiddenWindows);
        _trayService.ProfileRequested += profile =>
            Dispatcher.InvokeAsync(() => RunBackgroundActivation(profile, ActivationSource.Tray));

        _triggerService.Triggered += (profile, processName) => Dispatcher.InvokeAsync(() =>
        {
            SetStatus($"Авто-активация: запущен {processName}.exe");
            RunBackgroundActivation(profile, ActivationSource.Trigger);
        });
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateEditorVisibility();

        AdminBanner.Visibility = ElevationService.IsAdministrator
            ? Visibility.Collapsed
            : Visibility.Visible;

        _trayService.Initialize();
        _trayService.SetActiveProfile(null);
        RefreshTrayProfiles();

        if (_profiles.Count > 0)
            ProfileList.SelectedIndex = 0;

        // Хэндл окна уже создан, поэтому RegisterHotKey доступен.
        _hotkeysReady = _hotkeyService.Initialize(this);
        RebuildHotkeys();
        RebuildTriggerMonitor();

        _ = RefreshPowerPlanAsync();

        // Автозапуск и ошибки чтения показываем после того, как окно уже создано.
        SyncAutostartState();
        ReportLoadFailure();
        ReportStorageUnavailable();

        LoadHistory();
        UpdateUndoState();
        ShowActiveProfile();

        ApplyStartupProfile();
    }

    // ------------------------------------------------------------- профили

    private async void ProfileList_OnSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_changingSelection)
            return;

        if (_selectedProfile is not null && !_isBusy)
        {
            CopyEditorToProfile(_selectedProfile);
            await TrySaveProfilesAsync();
            RefreshBackgroundFeatures();
        }

        _selectedProfile = ProfileList.SelectedItem as ProfileModel;
        if (_selectedProfile is not null)
            LoadProfileToEditor(_selectedProfile);

        UpdateEditorVisibility();
        RefreshTrayProfiles();
    }

    private void LoadProfileToEditor(ProfileModel profile)
    {
        ProfileNameText.Text = profile.Name;
        DescriptionBox.Text = profile.Description;
        ReplaceCollection(_closeItems, profile.ProcessesToClose);
        ReplaceCollection(_startItems, profile.ProcessesToStart);
        ReplaceCollection(_urlItems, profile.UrlsToOpen);
        ReplaceCollection(_triggerItems, profile.TriggerProcesses);
        ReplaceCollection(_priorityRules, profile.PriorityRules);

        TriggerEnableCheck.IsChecked = profile.AutoActivateOnTrigger;
        NotifyCheck.IsChecked = profile.NotifyOnActivation;
        HotkeyBox.Text = profile.Hotkey;
        HotkeyStatusText.Text = string.IsNullOrWhiteSpace(profile.Hotkey)
            ? "Комбинация не задана"
            : "Сохраните профиль, чтобы применить комбинацию";

        var options = profile.Optimizations ?? new OptimizationOptions();
        options.Normalize();

        ClearTempCheck.IsChecked = options.ClearTempFiles;
        BrowserCacheCheck.IsChecked = options.ClearBrowserCache;
        RecycleBinCheck.IsChecked = options.EmptyRecycleBin;
        DnsCheck.IsChecked = options.FlushDns;
        PrefetchCheck.IsChecked = options.ClearPrefetch;
        SoftDistCheck.IsChecked = options.ClearSoftwareDistribution;
        StandbyCheck.IsChecked = options.ClearStandbyList;
        GameDvrCheck.IsChecked = options.DisableGameDvr;
        AnimationsCheck.IsChecked = options.DisableAnimations;

        ComboBoxHelper.SelectByTag(PowerPlanBox, options.PowerPlan);
        ComboBoxHelper.SelectByTag(SysMainBox, options.SysMain);
        ComboBoxHelper.SelectByTag(WindowActionBox, options.BackgroundWindowAction);

        SetStatus("Готово к работе");
    }

    private void CopyEditorToProfile(ProfileModel profile)
    {
        var options = new OptimizationOptions
        {
            ClearTempFiles = ClearTempCheck.IsChecked == true,
            ClearBrowserCache = BrowserCacheCheck.IsChecked == true,
            EmptyRecycleBin = RecycleBinCheck.IsChecked == true,
            FlushDns = DnsCheck.IsChecked == true,
            ClearPrefetch = PrefetchCheck.IsChecked == true,
            ClearSoftwareDistribution = SoftDistCheck.IsChecked == true,
            ClearStandbyList = StandbyCheck.IsChecked == true,
            DisableGameDvr = GameDvrCheck.IsChecked == true,
            DisableAnimations = AnimationsCheck.IsChecked == true,
            PowerPlan = ComboBoxHelper.GetTag(PowerPlanBox, PowerPlans.Keep),
            SysMain = ComboBoxHelper.GetTag(SysMainBox, SysMainModes.Keep),
            BackgroundWindowAction = ComboBoxHelper.GetTag(WindowActionBox, WindowActions.None)
        };
        options.Normalize();

        profile.Description = DescriptionBox.Text.Trim();
        profile.ProcessesToClose = [.. _closeItems];
        profile.ProcessesToStart = [.. _startItems];
        profile.UrlsToOpen = [.. _urlItems];
        profile.TriggerProcesses = [.. _triggerItems];
        profile.PriorityRules = [.. _priorityRules];
        profile.AutoActivateOnTrigger = TriggerEnableCheck.IsChecked == true;
        profile.NotifyOnActivation = NotifyCheck.IsChecked == true;
        profile.Hotkey = HotkeyBox.Text.Trim();
        profile.Optimizations = options;
        profile.Normalize();
    }

    private async void AddProfile_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AddProfileWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        if (_selectedProfile is not null)
            CopyEditorToProfile(_selectedProfile);

        var profile = new ProfileModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = dialog.ProfileName
        };

        _profiles.Add(profile);
        await TrySaveProfilesAsync();
        ProfileList.SelectedItem = profile;
        RefreshBackgroundFeatures();
    }

    private async void SaveProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
            return;

        CopyEditorToProfile(_selectedProfile);

        if (!await TrySaveProfilesAsync())
            return;

        RefreshBackgroundFeatures();
        SetStatus("Профиль сохранён", success: true);
    }

    private async void DeleteProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null)
            return;

        var answer = MessageBox.Show(
            this,
            $"Удалить профиль «{_selectedProfile.Name}»?",
            "ProfileOnPC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        var index = ProfileList.SelectedIndex;

        _changingSelection = true;
        _profiles.Remove(_selectedProfile);
        _selectedProfile = null;
        _changingSelection = false;

        await TrySaveProfilesAsync();

        if (_profiles.Count > 0)
            ProfileList.SelectedIndex = Math.Min(index, _profiles.Count - 1);
        else
            UpdateEditorVisibility();

        RefreshBackgroundFeatures();
    }

    // ------------------------------------------------------------ активация

    private async void ActivateProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile is null || _isBusy)
            return;

        var profile = _selectedProfile;

        _isBusy = true;
        SetBusy(true);

        try
        {
            var report = await ActivateProfileAsync(profile, ActivationSource.User);

            if (report is null)
                SetStatus("Активация уже выполняется");
            else
                ShowActivationReport(report);
        }
        catch (Exception exception)
        {
            SetStatus($"Ошибка активации: {exception.Message}");
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private async Task<ActivationReport?> ActivateProfileAsync(
        ProfileModel profile,
        ActivationSource source)
    {
        // Для выбранного профиля сначала фиксируем несохранённые правки из редактора.
        if (ReferenceEquals(profile, _selectedProfile))
        {
            CopyEditorToProfile(profile);
            // Активация продолжается даже при ошибке записи: профиль уже в памяти.
            await TrySaveProfilesAsync();
        }

        return await _activationService.ActivateAsync(profile, source);
    }

    private async void RunBackgroundActivation(ProfileModel profile, ActivationSource source)
    {
        try
        {
            var report = await ActivateProfileAsync(profile, source);

            if (report is null)
                SetStatus("Активация уже выполняется");
        }
        catch (Exception exception)
        {
            SetStatus($"Ошибка фоновой активации: {exception.Message}");
        }
    }

    private void OnActivationCompleted(ActivationReport report)
    {
        var errors = report.ErrorCount;

        SetStatus(
            errors == 0
                ? $"Профиль «{report.Profile.Name}» активирован за {report.Duration.TotalSeconds:0.0} с"
                : $"Профиль «{report.Profile.Name}»: выполнено {report.CompletedCount}, ошибок {errors}",
            success: errors == 0);

        _trayService.SetActiveProfile(report.Profile.Name);
        RefreshTrayProfiles(report.Profile.Id);
        _trayService.SetRestoreWindowsAvailable(_activationService.HasHiddenWindows);

        if (_settings.ActivationToastEnabled && report.Profile.NotifyOnActivation)
        {
            var summary = errors == 0
                ? $"Выполнено действий: {report.CompletedCount}"
                : $"Выполнено: {report.CompletedCount}, ошибок: {errors}";

            _trayService.ShowToast(
                $"Профиль «{report.Profile.Name}»",
                $"{summary} · {DescribeSource(report.Source)}",
                errors == 0 ? ToastKind.Info : ToastKind.Warning);
        }

        _ = RefreshPowerPlanAsync();
        UpdateUndoState();
        ShowActiveProfile(report.Profile);
        RememberActiveProfile(report);
        SaveHistoryEntry(report);
    }

    private static string DescribeSource(ActivationSource source) => source switch
    {
        ActivationSource.Hotkey => "горячая клавиша",
        ActivationSource.Trigger => "авто-активация",
        ActivationSource.Tray => "меню трея",
        ActivationSource.Startup => "старт приложения",
        _ => "вручную"
    };

    private void ShowActivationReport(ActivationReport report)
    {
        var errors = report.Errors;

        var message = $"Профиль «{report.Profile.Name}» активирован за {report.Duration.TotalSeconds:0.0} с.\n\n" +
                      $"Выполнено: {report.CompletedCount}\n" +
                      $"Пропущено: {report.SkippedCount}\n" +
                      $"Ошибок: {report.ErrorCount}";

        if (errors.Count > 0)
        {
            message += "\n\n" + string.Join(
                "\n",
                errors.Take(8).Select(result => "• " + result.Message));
        }

        MessageBox.Show(
            this,
            message,
            "ProfileOnPC",
            MessageBoxButton.OK,
            errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    // ---------------------------------------------------- очистка «сейчас»

    private async void RunOptimization_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = _selectedProfile;
        if (profile is null || _isBusy)
            return;

        CopyEditorToProfile(profile);

        _isBusy = true;
        SetBusy(true);

        try
        {
            SetStatus("Выполнение очистки…");

            var options = profile.Optimizations;
            var results = await Task.Run(() => _optimizationService.ApplyAsync(
                options,
                message => Dispatcher.InvokeAsync(() => SetStatus(message))));

            var errors = results.Count(result => !result.Success);
            SetStatus(
                $"Очистка выполнена: {results.Count(result => result.Success && !result.Skipped)}, ошибок {errors}",
                success: errors == 0);

            ShowResults("Очистка и оптимизация", results);
            await RefreshPowerPlanAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Ошибка очистки: {exception.Message}");
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
        }
    }

    private void ShowResults(string title, IReadOnlyList<ActionResult> results)
    {
        var errors = results.Count(result => !result.Success);

        var lines = results
            .Where(result => !result.Skipped)
            .Select(result => (result.Success ? "• " : "✕ ") + result.Message)
            .ToArray();

        var message = lines.Length == 0 ? "Действия не выбраны." : string.Join("\n", lines);

        MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            errors == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async Task RefreshPowerPlanAsync()
    {
        try
        {
            var active = await _optimizationService.Power.GetActiveAsync();
            PowerPlanStatusText.Text = active is null
                ? "Не удалось определить активную схему питания"
                : $"Сейчас активно: {active.Name}";
        }
        catch (Exception exception)
        {
            PowerPlanStatusText.Text = $"Схема питания недоступна: {exception.Message}";
        }
    }

    // ------------------------------------------------------- фон и триггеры

    private void RefreshBackgroundFeatures()
    {
        RebuildHotkeys();
        RebuildTriggerMonitor();
        RefreshTrayProfiles();
    }

    private void RefreshTrayProfiles(string? activeProfileId = null)
    {
        _trayService.UpdateProfiles(_profiles, activeProfileId ?? _selectedProfile?.Id);
    }

    private void RebuildHotkeys()
    {
        if (!_settings.GlobalHotkeysEnabled)
        {
            _hotkeyService.Rebuild([]);
            HotkeyStatusText.Text = "Глобальные горячие клавиши отключены в настройках";
            return;
        }

        if (!_hotkeysReady)
        {
            HotkeyStatusText.Text = "Глобальные горячие клавиши недоступны";
            return;
        }

        var bindings = _profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Hotkey))
            .Select(profile => (
                Gesture: profile.Hotkey,
                Callback: (Action)(() => RunBackgroundActivation(profile, ActivationSource.Hotkey))))
            .ToArray();

        var result = _hotkeyService.Rebuild(bindings);

        HotkeyStatusText.Text = result.Failed.Count > 0
            ? $"Не зарегистрированы: {string.Join("; ", result.Failed)}"
            : result.Registered.Count > 0
                ? $"Активных комбинаций: {result.Registered.Count}"
                : "Комбинации не заданы";
    }

    private void RebuildTriggerMonitor()
    {
        if (!_settings.ProcessTriggersEnabled)
        {
            _triggerService.Stop();
            return;
        }

        _triggerService.UpdateProfiles(_profiles.Where(profile => profile.AutoActivateOnTrigger));
        _triggerService.Start(TimeSpan.FromSeconds(2));
    }

    private void HotkeyBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Tab и Esc оставляем навигации по интерфейсу.
        if (key is Key.Tab or Key.Escape)
            return;

        e.Handled = true;

        if (key is Key.Back or Key.Delete)
        {
            HotkeyBox.Text = string.Empty;
            HotkeyStatusText.Text = "Комбинация очищена — сохраните профиль";
            return;
        }

        if (HotkeyGesture.FromKey(key, Keyboard.Modifiers) is { } gesture)
        {
            HotkeyBox.Text = gesture;
            HotkeyStatusText.Text = "Сохраните профиль, чтобы применить комбинацию";
            return;
        }

        HotkeyStatusText.Text = "Нужна комбинация с Ctrl, Alt или Win";
    }

    private void ClearHotkey_OnClick(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = string.Empty;
        HotkeyStatusText.Text = "Комбинация очищена — сохраните профиль";
        HotkeyBox.Focus();
    }

    // --------------------------------------------------------- редактор

    private void AddClose_OnClick(object sender, RoutedEventArgs e) =>
        AddUnique(_closeItems, NormalizeCloseProcess(CloseInput.Text), CloseInput);

    private void AddStart_OnClick(object sender, RoutedEventArgs e) =>
        AddUnique(_startItems, StartInput.Text.Trim().Trim('"'), StartInput);

    private void AddUrl_OnClick(object sender, RoutedEventArgs e)
    {
        var value = UrlInput.Text.Trim();
        if (!string.IsNullOrWhiteSpace(value) &&
            !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        AddUnique(_urlItems, value, UrlInput);
    }

    private void AddTrigger_OnClick(object sender, RoutedEventArgs e) =>
        AddUnique(_triggerItems, ProcessPriorityService.NormalizeName(TriggerInput.Text), TriggerInput);

    private void AddPriority_OnClick(object sender, RoutedEventArgs e)
    {
        var name = ProcessPriorityService.NormalizeName(PriorityInput.Text);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!_priorityRules.Any(rule =>
                rule.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            _priorityRules.Add(new ProcessPriorityRule
            {
                ProcessName = name,
                Priority = ProcessPriorities.Normal
            });
        }

        PriorityInput.Clear();
        PriorityInput.Focus();
    }

    private void BrowseClose_OnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseForExecutable();
        if (path is not null)
            AddUnique(_closeItems, Path.GetFileName(path), CloseInput);
    }

    private void BrowseStart_OnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseForExecutable();
        if (path is not null)
            AddUnique(_startItems, path, StartInput);
    }

    private void BrowseTrigger_OnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseForExecutable();
        if (path is null)
            return;

        TriggerInput.Text = Path.GetFileName(path);
        AddTrigger_OnClick(sender, e);
    }

    private void BrowsePriority_OnClick(object sender, RoutedEventArgs e)
    {
        var path = BrowseForExecutable();
        if (path is null)
            return;

        PriorityInput.Text = Path.GetFileName(path);
        AddPriority_OnClick(sender, e);
    }

    private void RemoveItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        switch (button.CommandParameter?.ToString())
        {
            case "Close":
                if (button.Tag is string closeValue)
                    _closeItems.Remove(closeValue);
                break;
            case "Start":
                if (button.Tag is string startValue)
                    _startItems.Remove(startValue);
                break;
            case "Url":
                if (button.Tag is string urlValue)
                    _urlItems.Remove(urlValue);
                break;
            case "Trigger":
                if (button.Tag is string triggerValue)
                    _triggerItems.Remove(triggerValue);
                break;
            case "Priority":
                if (button.Tag is ProcessPriorityRule rule)
                    _priorityRules.Remove(rule);
                break;
        }
    }

    private void CloseInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddClose_OnClick(sender, e);
    }

    private void StartInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddStart_OnClick(sender, e);
    }

    private void UrlInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddUrl_OnClick(sender, e);
    }

    private void TriggerInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddTrigger_OnClick(sender, e);
    }

    private void PriorityInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddPriority_OnClick(sender, e);
    }

    // ------------------------------------------------------------ пресеты

    private void GamingPreset_OnClick(object sender, RoutedEventArgs e)
    {
        ClearTempCheck.IsChecked = true;
        BrowserCacheCheck.IsChecked = true;
        RecycleBinCheck.IsChecked = false;
        DnsCheck.IsChecked = true;
        PrefetchCheck.IsChecked = true;
        SoftDistCheck.IsChecked = false;
        StandbyCheck.IsChecked = true;
        GameDvrCheck.IsChecked = true;
        AnimationsCheck.IsChecked = true;

        ComboBoxHelper.SelectByTag(PowerPlanBox, PowerPlans.HighPerformance);
        ComboBoxHelper.SelectByTag(SysMainBox, SysMainModes.Keep);
        ComboBoxHelper.SelectByTag(WindowActionBox, WindowActions.Minimize);

        SetStatus("Выбран игровой пресет — нажмите «Сохранить»");
    }

    private void CleanupPreset_OnClick(object sender, RoutedEventArgs e)
    {
        ClearTempCheck.IsChecked = true;
        BrowserCacheCheck.IsChecked = true;
        RecycleBinCheck.IsChecked = true;
        DnsCheck.IsChecked = true;
        PrefetchCheck.IsChecked = true;
        SoftDistCheck.IsChecked = true;
        StandbyCheck.IsChecked = true;
        GameDvrCheck.IsChecked = false;
        AnimationsCheck.IsChecked = false;

        ComboBoxHelper.SelectByTag(PowerPlanBox, PowerPlans.Keep);
        ComboBoxHelper.SelectByTag(SysMainBox, SysMainModes.Keep);
        ComboBoxHelper.SelectByTag(WindowActionBox, WindowActions.None);

        SetStatus("Выбран пресет очистки — нажмите «Сохранить»");
    }

    private void ResetPreset_OnClick(object sender, RoutedEventArgs e)
    {
        ClearTempCheck.IsChecked = false;
        BrowserCacheCheck.IsChecked = false;
        RecycleBinCheck.IsChecked = false;
        DnsCheck.IsChecked = false;
        PrefetchCheck.IsChecked = false;
        SoftDistCheck.IsChecked = false;
        StandbyCheck.IsChecked = false;
        GameDvrCheck.IsChecked = false;
        AnimationsCheck.IsChecked = false;

        ComboBoxHelper.SelectByTag(PowerPlanBox, PowerPlans.Keep);
        ComboBoxHelper.SelectByTag(SysMainBox, SysMainModes.Keep);
        ComboBoxHelper.SelectByTag(WindowActionBox, WindowActions.None);

        SetStatus("Все действия оптимизации сняты");
    }

    // ------------------------------------------------------------ настройки

    private async void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var requestedAutostart = dialog.Settings.StartWithWindows;
        var autostartChanged = requestedAutostart != _settings.StartWithWindows;

        _settings = dialog.Settings;
        ThemeService.Apply(_settings.Theme, _settings.AccentColor);

        // 1. Автозапуск: отказ в правах не должен мешать сохранению остальных настроек.
        string? autostartError = null;
        if (autostartChanged && !AutorunService.TrySetEnabled(requestedAutostart, out var error))
            autostartError = error;

        // 2. Флаг в настройках приводим к фактическому состоянию реестра.
        if (AutorunService.TryGetEnabled(out var actualAutostart, out _))
            _settings.StartWithWindows = actualAutostart;

        // 3. Настройки сохраняются независимо от результата работы с реестром.
        var save = await _storage.SaveSettingsAsync(_settings);
        if (save.Success)
        {
            SetStatus("Настройки сохранены", success: true);
        }
        else
        {
            SetStatus("Не удалось сохранить настройки");
            ShowStorageWarningOnce("Не удалось сохранить настройки", save.Error);
        }

        RefreshBackgroundFeatures();

        if (autostartError is not null
            || (autostartChanged && _settings.StartWithWindows != requestedAutostart))
        {
            MessageBox.Show(
                this,
                "Настройки сохранены, но автозапуск изменить не удалось.\n\n" +
                (autostartError ?? "Реестр не подтвердил изменение.") +
                "\n\nПриложение продолжит работать без автозапуска.",
                "ProfileOnPC",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Elevate_OnClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Приложение будет перезапущено с правами администратора, текущее окно закроется.",
            "ProfileOnPC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        if (ElevationService.TryRelaunchAsAdministrator(out var error))
        {
            ShutdownApplication();
            return;
        }

        MessageBox.Show(
            this,
            $"Не удалось перезапустить приложение: {error}",
            "ProfileOnPC",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    // ---------------------------------------------------------------- трей

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowExit)
            return;

        var action = CloseActions.Normalize(_settings.CloseAction);
        AppLog.Info($"Закрытие окна: действие «{action}»");

        // Свёрнутое на панель задач окно всегда доступно — безопасный вариант,
        // если иконка в трее скрыта в области переполнения «^».
        if (action == CloseActions.Minimize)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }

        if (action == CloseActions.Tray)
        {
            e.Cancel = true;
            Hide();

            _trayService.ShowToast(
                "ProfileOnPC",
                "Приложение работает в трее. Иконка может быть скрыта в области «^» рядом с часами; " +
                "чтобы открыть окно, запустите ProfileOnPC.exe ещё раз.");

            return;
        }

        _allowExit = true;
        ReleaseResources();
        Application.Current.Shutdown();
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleaseResources();
        base.OnClosed(e);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void RestoreHiddenWindows()
    {
        var restored = _activationService.RestoreHiddenWindows();
        _trayService.SetRestoreWindowsAvailable(_activationService.HasHiddenWindows);

        SetStatus(
            restored > 0 ? $"Восстановлено окон: {restored}" : "Скрытых окон нет",
            success: restored > 0);
    }

    private void ShutdownApplication()
    {
        _allowExit = true;
        ReleaseResources();
        Application.Current.Shutdown();
    }

    private void ReleaseResources()
    {
        if (_resourcesReleased)
            return;

        _resourcesReleased = true;

        _triggerService.Dispose();
        _hotkeyService.Dispose();
        _activationService.Dispose();
        _trayService.Dispose();
    }

    // ------------------------------------------------------- окно и трей

    /// <summary>Показывает окно (вызывается при повторном запуске exe).</summary>
    public void ShowFromTray()
    {
        AppLog.Info("Показываю окно по запросу второго запуска");
        RestoreFromTray();
    }

    // ------------------------------------------------------- откат активации

    private void UpdateUndoState()
    {
        UndoButton.IsEnabled = !_isBusy && _activationService.CanRestore;
        UndoButton.ToolTip = _activationService.CanRestore
            ? "Вернуть схему питания, SysMain, Game DVR и скрытые окна к состоянию до активации"
            : "Нечего откатывать: профиль не менял состояние системы";
    }

    private async void UndoActivation_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !_activationService.CanRestore)
            return;

        _isBusy = true;
        SetBusy(true);

        try
        {
            SetStatus("Возвращаю состояние системы…");
            var results = await _activationService.RestoreLastStateAsync();
            var errors = results.Count(result => !result.Success);

            SetStatus(
                errors == 0
                    ? "Состояние системы восстановлено"
                    : $"Откат выполнен с ошибками: {errors}",
                success: errors == 0);

            AppLog.Info($"Откат активации: {string.Join("; ", results.Select(result => result.Message))}");
            ShowResults("Возврат к прежнему состоянию", results);
        }
        catch (Exception exception)
        {
            AppLog.Error("Ошибка отката активации", exception);
            SetStatus($"Ошибка отката: {exception.Message}");
        }
        finally
        {
            _isBusy = false;
            SetBusy(false);
            UpdateUndoState();
        }
    }

    // ---------------------------------------------------- история активаций

    private void LoadHistory()
    {
        _historyEntries.Clear();

        foreach (var entry in _historyService.Load())
            _historyEntries.Add(entry);

        HistoryCountText.Text = _historyEntries.Count == 0
            ? "Пока нет активаций"
            : $"Записей: {_historyEntries.Count}";
    }

    private void SaveHistoryEntry(ActivationReport report)
    {
        var entry = new ActivationHistoryEntry
        {
            Timestamp = DateTime.Now,
            ProfileId = report.Profile.Id,
            ProfileName = report.Profile.Name,
            Source = DescribeSource(report.Source),
            DurationMs = Math.Round(report.Duration.TotalMilliseconds),
            Context = ElevationService.DescribeSecurityContext(),
            Completed = report.CompletedCount,
            Skipped = report.SkippedCount,
            Errors = report.ErrorCount,
            Steps = [.. report.Results.Select(result => new ActivationStepRecord
            {
                Success = result.Success,
                Skipped = result.Skipped,
                Message = result.Message
            })]
        };

        _historyEntries.Insert(0, entry);

        while (_historyEntries.Count > HistoryService.MaxEntries)
            _historyEntries.RemoveAt(_historyEntries.Count - 1);

        AppLog.Info(
            $"Активация «{entry.ProfileName}» ({entry.Source}): выполнено {entry.Completed}, " +
            $"пропущено {entry.Skipped}, ошибок {entry.Errors}");

        _ = _historyService.AddAsync(entry);
    }

    private void ShowActiveProfile(ProfileModel? profile = null)
    {
        var target = profile
                     ?? _profiles.FirstOrDefault(item =>
                         string.Equals(item.Id, _settings.ActiveProfileId, StringComparison.Ordinal));

        if (target is null)
        {
            ActiveProfileText.Visibility = Visibility.Collapsed;
            return;
        }

        ActiveProfileText.Text = $"Активен: {target.Name}";
        ActiveProfileText.Visibility = Visibility.Visible;
    }

    private void RememberActiveProfile(ActivationReport report)
    {
        if (string.Equals(_settings.ActiveProfileId, report.Profile.Id, StringComparison.Ordinal))
            return;

        _settings.ActiveProfileId = report.Profile.Id;
        _ = PersistSettingsAsync();
    }

    private async Task PersistSettingsAsync()
    {
        var result = await _storage.SaveSettingsAsync(_settings);
        if (!result.Success)
            AppLog.Warn($"Не удалось сохранить настройки: {result.Error}");
    }

    private void HistoryList_OnSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        HistoryDetailsText.Text = HistoryList.SelectedItem is ActivationHistoryEntry entry
            ? entry.Details
            : "Выберите запись слева, чтобы увидеть подробности.";
    }

    private void ClearHistory_OnClick(object sender, RoutedEventArgs e)
    {
        if (_historyEntries.Count == 0)
        {
            SetStatus("История уже пуста");
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Очистить историю активаций?",
            "ProfileOnPC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        _historyService.Clear();
        LoadHistory();
        HistoryDetailsText.Text = "История очищена.";
        AppLog.Info("История активаций очищена");
        SetStatus("История очищена", success: true);
    }

    private void OpenLogFolder_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppLog.DirectoryPath);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppLog.DirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AppLog.Error("Не удалось открыть папку журнала", exception);
            MessageBox.Show(
                this,
                $"Не удалось открыть папку журнала:\n{exception.Message}\n\nПуть: {AppLog.DirectoryPath}",
                "ProfileOnPC",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyStartupProfile()
    {
        if (!_settings.ApplyProfileOnStartup || string.IsNullOrWhiteSpace(_settings.ActiveProfileId))
            return;

        var profile = _profiles.FirstOrDefault(item =>
            string.Equals(item.Id, _settings.ActiveProfileId, StringComparison.Ordinal));

        if (profile is null)
        {
            AppLog.Warn($"Активный профиль «{_settings.ActiveProfileId}» не найден — старт без активации");
            return;
        }

        AppLog.Info($"Применяю активный профиль «{profile.Name}» при старте приложения");
        RunBackgroundActivation(profile, ActivationSource.Startup);
    }

    // --------------------------------------------------- сохранение данных

    private async Task<bool> TrySaveProfilesAsync()
    {
        var result = await _storage.SaveProfilesAsync(_profiles);
        if (result.Success)
            return true;

        AppLog.Warn($"Не удалось сохранить профили: {result.Error}");
        SetStatus("Не удалось сохранить профили");
        ShowStorageWarningOnce("Не удалось сохранить профили", result.Error);
        return false;
    }

    private void ShowStorageWarningOnce(string title, string? details)
    {
        // Одно предупреждение за сессию: иначе каждое переключение профиля даёт диалог.
        if (_storageWarningShown)
            return;

        _storageWarningShown = true;

        MessageBox.Show(
            this,
            $"{details}\n\n" +
            "Изменения остаются в памяти — приложение продолжает работать.\n" +
            $"Проверьте права на папку «{_storage.DataDirectory}» и настройки антивируса.\n" +
            "Если exe запущен из терминала, IDE или песочницы с ограничениями — " +
            "закройте его и запустите обычным двойным щелчком.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void SyncAutostartState()
    {
        // Галочка автозапуска должна отражать реестр, а не прошлые ожидания.
        if (!AutorunService.TryGetEnabled(out var enabled, out _))
            return;

        _settings.StartWithWindows = enabled;

        if (!enabled)
            return;

        // Если сборку перенесли в другую папку, автозапуск должен указывать на текущий exe.
        if (AutorunService.TryGetCommand(out var stored, out _)
            && !string.Equals(stored, AutorunService.CurrentCommand, StringComparison.OrdinalIgnoreCase))
        {
            AutorunService.TrySetEnabled(true, out _);
        }
    }

    private void ReportLoadFailure()
    {
        if (string.IsNullOrWhiteSpace(_storage.LastLoadError))
            return;

        SetStatus("Не удалось прочитать сохранённые данные");
        ShowStorageWarningOnce("Не удалось прочитать данные", _storage.LastLoadError);
    }

    private void ReportStorageUnavailable()
    {
        if (_storage.IsWritable)
            return;

        var details =
            $"Папка «{_storage.DataDirectory}» недоступна для записи.\n" +
            $"Причина: {_storage.WriteProbeError}\n" +
            $"Контекст запуска: {ElevationService.DescribeSecurityContext()}.";

        // Самая частая причина в собранных локально сборках: exe унаследовал метку
        // Low integrity от папки, где его собрали или скачали.
        if (ElevationService.GetIntegrityLevelName() == "Low")
        {
            details += "\n\nПриложение запущено с низким уровнем целостности (Low integrity), " +
                       "поэтому Windows запрещает ему писать в профиль пользователя.\n" +
                       "Скопируйте папку с программой в обычный каталог (например, C:\\Users\\<вы>\\ProfileOnPC) " +
                       "или снимите метку командой:\n" +
                       "icacls \"<папка с программой>\" /setintegritylevel (OI)(CI)Medium /T";
        }

        SetStatus("Папка данных недоступна для записи — изменения не сохраняются");
        ShowStorageWarningOnce("Нельзя сохранять данные", details);
    }

    // ------------------------------------------------------------- утилиты

    private void UpdateEditorVisibility()
    {
        var hasProfile = _selectedProfile is not null;
        WelcomePanel.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible;
        EditorPanel.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy)
    {
        ActivateButton.IsEnabled = !busy;
        UndoButton.IsEnabled = !busy && _activationService.CanRestore;
        ActivityProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStatus(string message, bool success = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = success
            ? (System.Windows.Media.Brush)FindResource("SuccessBrush")
            : (System.Windows.Media.Brush)FindResource("SecondaryTextBrush");
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }

    private static void AddUnique(
        ObservableCollection<string> collection,
        string value,
        System.Windows.Controls.TextBox input)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!collection.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
            collection.Add(value);

        input.Clear();
        input.Focus();
    }

    private static string NormalizeCloseProcess(string value)
    {
        var normalized = value.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : Path.GetFileName(normalized);
    }

    private static string? BrowseForExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите приложение",
            Filter = "Приложения (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
