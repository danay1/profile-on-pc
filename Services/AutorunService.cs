using System.Security;
using Microsoft.Win32;

namespace ProfileOnPC.Services;

/// <summary>
/// Автозапуск через HKCU\...\Run. Все операции возвращают результат и никогда не выбрасывают
/// исключение доступа: отказ в правах — это предупреждение для пользователя, а не падение.
/// </summary>
public static class AutorunService
{
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ValueName = "ProfileOnPC";

    /// <summary>
    /// Включает или выключает автозапуск.
    /// Возвращает false и текст причины, если раздел реестра недоступен на запись.
    /// </summary>
    public static bool TrySetEnabled(bool enabled, out string error)
    {
        error = string.Empty;

        try
        {
            // writable: true — открываем раздел именно для записи (при необходимости он создаётся).
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            if (key is null)
            {
                error = $"Раздел HKCU\\{RegistryPath} недоступен для записи.";
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            key.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"Нет прав на изменение автозапуска (HKCU\\{RegistryPath}). {exception.Message}";
            return false;
        }
        catch (SecurityException exception)
        {
            error = $"Политика безопасности запрещает изменять автозапуск. {exception.Message}";
            return false;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            error = $"Не удалось изменить автозапуск: {exception.Message}";
            return false;
        }
    }

    /// <summary>Читает фактическое состояние автозапуска (а не то, что записано в настройках).</summary>
    public static bool TryGetEnabled(out bool enabled, out string error)
    {
        enabled = false;
        error = string.Empty;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            if (key is null)
                return true;

            enabled = key.GetValue(ValueName) is not null;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Возвращает команду, записанную в автозапуске (null — значения нет).</summary>
    public static bool TryGetCommand(out string? command, out string error)
    {
        command = null;
        error = string.Empty;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            command = key?.GetValue(ValueName) as string;
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Команда, которая должна стоять в автозапуске для текущего расположения exe.</summary>
    public static string CurrentCommand => BuildLaunchCommand();

    private static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(processPath))
            return string.Empty;

        if (!Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return $"\"{processPath}\"";

        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "ProfileOnPC.dll");
        return $"\"{processPath}\" \"{assemblyPath}\"";
    }
}
