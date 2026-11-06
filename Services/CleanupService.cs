using System.Runtime.InteropServices;
using ProfileOnPC.Models;
using ProfileOnPC.Services.Native;

namespace ProfileOnPC.Services;

/// <summary>
/// Очистка кэшей Windows и сброс списка ожидания RAM.
/// Все методы возвращают <see cref="ActionResult"/> и не выбрасывают исключений.
/// </summary>
public sealed class CleanupService
{
    private const uint ShrbNoConfirmation = 0x1;
    private const uint ShrbNoProgressUi = 0x2;
    private const uint ShrbNoSound = 0x4;

    private const int StatusSuccess = 0;
    private const int StatusPrivilegeNotHeld = unchecked((int)0xC0000061);
    private const int StatusAccessDenied = unchecked((int)0xC0000022);

    private readonly SystemServiceManager _services;

    public CleanupService(SystemServiceManager services) => _services = services;

    private static string WindowsPath => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // ------------------------------------------------------------ базовые

    public ActionResult ClearTempFiles()
    {
        var removed = 0;
        var skipped = 0;

        var paths = new[]
        {
            Path.GetTempPath(),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Temp")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
            ClearDirectory(path, ref removed, ref skipped);

        return new ActionResult(
            true,
            removed == 0,
            removed == 0
                ? "Временные файлы уже очищены"
                : $"Временные файлы: удалено {removed}, пропущено {skipped}");
    }

    public ActionResult ClearBrowserCache()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var targets = new List<string>
        {
            Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"),
            Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Code Cache"),
            Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"),
            Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Code Cache")
        };

        var firefoxProfiles = Path.Combine(roaming, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (var profile in Directory.EnumerateDirectories(firefoxProfiles))
            {
                targets.Add(Path.Combine(profile, "cache2"));
                targets.Add(Path.Combine(profile, "startupCache"));
            }
        }

        var removed = 0;
        var skipped = 0;

        foreach (var path in targets)
            ClearDirectory(path, ref removed, ref skipped);

        return new ActionResult(
            true,
            removed == 0,
            removed == 0
                ? "Кеш браузеров уже очищен или не найден"
                : $"Кеш браузеров: удалено {removed}, пропущено {skipped}");
    }

    public ActionResult EmptyRecycleBin()
    {
        var result = NativeMethods.SHEmptyRecycleBin(
            IntPtr.Zero,
            null,
            ShrbNoConfirmation | ShrbNoProgressUi | ShrbNoSound);

        return result is 0 or 1
            ? new ActionResult(true, result == 1, result == 1 ? "Корзина уже пуста" : "Корзина очищена")
            : ActionResult.Fail($"Не удалось очистить корзину: 0x{result:X8}");
    }

    // ---------------------------------------------------------------- DNS

    public async Task<ActionResult> FlushDnsAsync(CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner
            .RunAsync("ipconfig.exe", "/flushdns", cancellationToken)
            .ConfigureAwait(false);

        return result.Success
            ? ActionResult.Ok("Кэш DNS сброшен")
            : ActionResult.Fail($"Не удалось сбросить кэш DNS: {ProcessRunner.Compact(result.Diagnostic)}");
    }

    // ----------------------------------------------------------- Prefetch

    public ActionResult ClearPrefetch()
    {
        if (!ElevationService.IsAdministrator)
            return ActionResult.Fail("Очистка Prefetch требует прав администратора");

        var directory = Path.Combine(WindowsPath, "Prefetch");
        if (!Directory.Exists(directory))
            return ActionResult.Skip("Папка Prefetch не найдена");

        var removed = 0;
        var skipped = 0;

        try
        {
            // Удаляем только файлы предзагрузки, Layout.ini и ReadyBoot не трогаем.
            foreach (var file in Directory.EnumerateFiles(directory, "*.pf"))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch
                {
                    skipped++;
                }
            }
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"Не удалось очистить Prefetch: {exception.Message}");
        }

        return removed == 0
            ? ActionResult.Skip("Prefetch уже очищен или файлы заняты")
            : ActionResult.Ok($"Prefetch: удалено {removed}, пропущено {skipped}");
    }

    // ------------------------------------------------- SoftwareDistribution

    public async Task<ActionResult> ClearSoftwareDistributionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ElevationService.IsAdministrator)
            return ActionResult.Fail("Очистка SoftwareDistribution требует прав администратора");

        var download = Path.Combine(WindowsPath, "SoftwareDistribution", "Download");
        if (!Directory.Exists(download))
            return ActionResult.Skip("Папка SoftwareDistribution\\Download не найдена");

        var stoppedServices = new List<string>();

        try
        {
            // Без остановки служб Windows Update файлы заблокированы.
            foreach (var serviceName in new[] { "wuauserv", "bits" })
            {
                var stop = await _services
                    .StopAsync(serviceName, TimeSpan.FromSeconds(15), cancellationToken)
                    .ConfigureAwait(false);

                if (stop.Success && !stop.Skipped)
                    stoppedServices.Add(serviceName);
            }

            var removed = 0;
            var skipped = 0;
            ClearDirectory(download, ref removed, ref skipped);

            return removed == 0
                ? ActionResult.Skip("SoftwareDistribution уже очищена или файлы заняты")
                : ActionResult.Ok($"SoftwareDistribution: удалено {removed}, пропущено {skipped}");
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"Не удалось очистить SoftwareDistribution: {exception.Message}");
        }
        finally
        {
            // Возвращаем службы в рабочее состояние даже при ошибке или отмене.
            foreach (var serviceName in stoppedServices)
            {
                await _services
                    .StartAsync(serviceName, TimeSpan.FromSeconds(15), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }

    // ------------------------------------------------------- Standby List

    /// <summary>
    /// Сбрасывает список ожидания (кэш RAM) через NtSetSystemInformation.
    /// Требует привилегию SeProfileSingleProcessPrivilege.
    /// </summary>
    public ActionResult ClearStandbyList()
    {
        if (!ElevationService.TryEnablePrivilege("SeProfileSingleProcessPrivilege", out var privilegeError))
            return ActionResult.Fail($"Не удалось получить привилегию для очистки RAM: {privilegeError}");

        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(buffer, NativeMethods.MemoryPurgeStandbyList);

            var status = NativeMethods.NtSetSystemInformation(
                NativeMethods.SystemMemoryListInformation,
                buffer,
                sizeof(int));

            return status switch
            {
                StatusSuccess => ActionResult.Ok("Список ожидания (Standby List) очищен"),
                StatusPrivilegeNotHeld => ActionResult.Fail(
                    "Windows отклонил очистку: нужны права администратора"),
                StatusAccessDenied => ActionResult.Fail(
                    "Windows отклонил очистку: доступ запрещён, запустите приложение от администратора"),
                _ => ActionResult.Fail($"NtSetSystemInformation вернул 0x{status:X8}")
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ------------------------------------------------------------ helpers

    private static void ClearDirectory(string path, ref int removed, ref int skipped)
    {
        if (!Directory.Exists(path))
            return;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(path).ToArray();
        }
        catch
        {
            skipped++;
            return;
        }

        foreach (var entry in entries)
        {
            try
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, true);
                else
                    File.Delete(entry);

                removed++;
            }
            catch
            {
                skipped++;
            }
        }
    }
}
