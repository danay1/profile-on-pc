using System.Windows;
using ProfileOnPC.Services;

namespace ProfileOnPC;

public partial class App : System.Windows.Application
{
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.Info("=== Запуск ProfileOnPC ===");
        AppLog.Info($"Контекст: {ElevationService.DescribeSecurityContext()}");

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Необработанное исключение интерфейса", args.Exception);

            MessageBox.Show(
                $"Произошла непредвиденная ошибка:\n\n{args.Exception.Message}\n\n" +
                $"Подробности записаны в журнал:\n{AppLog.DirectoryPath}",
                "ProfileOnPC",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Необработанное исключение (вне UI)", args.ExceptionObject as Exception);

        // Второй запуск не создаёт новый процесс, а показывает окно уже работающего.
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            AppLog.Info("Приложение уже запущено — активирую существующее окно");
            _singleInstance.SignalExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        var storage = new StorageService();
        if (!string.IsNullOrWhiteSpace(storage.LastLoadError))
            AppLog.Warn(storage.LastLoadError!);

        AppLog.Info($"Папка данных: {storage.DataDirectory} (запись: {storage.IsWritable})");

        var settings = storage.LoadSettings();
        ThemeService.Apply(settings.Theme, settings.AccentColor);

        var window = new MainWindow(storage, settings);
        MainWindow = window;

        _singleInstance.ShowRequested += () => Dispatcher.InvokeAsync(window.ShowFromTray);

        window.Show();
        AppLog.Info("Окно создано");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"=== Выход, код {e.ApplicationExitCode} ===");
        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }
}
