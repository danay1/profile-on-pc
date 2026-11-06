using System.ComponentModel;
using System.Diagnostics;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>
/// Управление приоритетом CPU уже запущенных процессов профиля (Process.PriorityClass).
/// </summary>
public sealed class ProcessPriorityService
{
    public ActionResult Apply(string processName, string priority)
    {
        var name = NormalizeName(processName);
        if (name.Length == 0)
            return ActionResult.Fail("Не указано имя процесса для приоритета");

        var priorityClass = ProcessPriorities.ToPriorityClass(priority);
        var label = ProcessPriorities.GetLabel(priority);

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(name);
        }
        catch (Exception exception)
        {
            return ActionResult.Fail($"Не удалось получить список процессов {name}.exe: {exception.Message}");
        }

        if (processes.Length == 0)
            return ActionResult.Skip($"{name}.exe не запущен — приоритет не изменён");

        var changed = 0;
        var denied = 0;

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    // Приоритет нельзя менять у защищённых системных процессов и при чужом уровне целостности.
                    process.PriorityClass = priorityClass;
                    changed++;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    denied++;
                }
            }
        }

        if (changed == 0)
        {
            return ActionResult.Fail(
                $"Не удалось изменить приоритет {name}.exe (нет прав или процесс защищён Windows)");
        }

        return denied == 0
            ? ActionResult.Ok($"{name}.exe: приоритет «{label}» для {changed} процесс(ов)")
            : ActionResult.Ok($"{name}.exe: приоритет «{label}» для {changed}, без прав {denied}");
    }

    public static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().Trim('"');
        var fileName = Path.GetFileName(normalized);
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return withoutExtension.Trim();
    }
}
