using System.Diagnostics;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>Закрытие, запуск процессов и открытие ссылок профиля.</summary>
public sealed class ProcessService
{
    public async Task<ActionResult> CloseAsync(string value)
    {
        var processName = GetProcessName(value);
        if (string.IsNullOrWhiteSpace(processName))
            return ActionResult.Fail("Не указано имя процесса");

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"Не удалось получить процессы {processName}.exe: {exception.Message}");
        }

        if (processes.Length == 0)
            return ActionResult.Skip($"{processName}.exe уже не запущен");

        var closed = 0;

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    process.Kill(true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    closed++;
                }
                catch
                {
                    // Продолжаем закрывать остальные экземпляры.
                }
            }
        }

        return closed > 0
            ? ActionResult.Ok($"Закрыто экземпляров {processName}.exe: {closed}")
            : ActionResult.Fail($"Не удалось закрыть {processName}.exe");
    }

    public Task<ActionResult> StartAsync(string value)
    {
        var processName = GetProcessName(value);
        if (!string.IsNullOrWhiteSpace(processName) &&
            Process.GetProcessesByName(processName).Length > 0)
        {
            return Task.FromResult(ActionResult.Skip($"{processName}.exe уже запущен"));
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = value.Trim().Trim('"'),
                UseShellExecute = true
            };

            Process.Start(startInfo);
            return Task.FromResult(ActionResult.Ok($"Запущено: {value}"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ActionResult.Fail($"Не удалось запустить {value}: {exception.Message}"));
        }
    }

    public Task<ActionResult> OpenUrlAsync(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Task.FromResult(ActionResult.Fail($"Некорректная ссылка: {value}"));
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(ActionResult.Ok($"Открыто: {uri.AbsoluteUri}"));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ActionResult.Fail($"Не удалось открыть ссылку: {exception.Message}"));
        }
    }

    private static string GetProcessName(string value)
    {
        var normalized = value.Trim().Trim('"');
        var fileName = Path.GetFileName(normalized);
        return Path.GetFileNameWithoutExtension(fileName);
    }
}
