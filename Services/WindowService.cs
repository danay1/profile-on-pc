using System.Diagnostics;
using System.Text;
using ProfileOnPC.Services.Native;

namespace ProfileOnPC.Services;

/// <summary>Окно верхнего уровня, которое было свернуто или скрыто при активации профиля.</summary>
public sealed record CapturedWindow(
    IntPtr Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    bool WasMaximized);

/// <summary>
/// Поиск окон фоновых приложений и их сворачивание/скрытие «в трей».
/// Честное ограничение: приложение не может стать иконкой трея вместо чужой программы,
/// поэтому режим «скрыть» убирает окно с панели задач, а вернуть его можно из меню
/// значка ProfileOnPC (или автоматически при следующей активации/выходе).
/// </summary>
public sealed class WindowService
{
    private static readonly HashSet<string> IgnoredWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "TaskListThumbnailWnd"
    };

    /// <summary>
    /// Собирает видимые окна с заголовком, кроме собственных окон приложения
    /// и процессов из <paramref name="keepProcessNames"/>.
    /// </summary>
    public IReadOnlyList<CapturedWindow> CaptureBackgroundWindows(IEnumerable<string> keepProcessNames)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in keepProcessNames)
        {
            var normalized = ProcessPriorityService.NormalizeName(name);
            if (normalized.Length > 0)
                keep.Add(normalized);
        }

        var ownProcessId = (uint)Environment.ProcessId;
        var windows = new List<CapturedWindow>();

        NativeMethods.EnumWindowsProc callback = (handle, _) =>
        {
            try
            {
                CollectWindow(handle, ownProcessId, keep, windows);
            }
            catch
            {
                // Одно недоступное окно не должно прерывать перебор остальных.
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        return windows;
    }

    /// <summary>Скрывает окна (окно исчезает с панели задач, процесс продолжает работать).</summary>
    public IReadOnlyList<CapturedWindow> Hide(IEnumerable<CapturedWindow> windows)
    {
        var hidden = new List<CapturedWindow>();

        foreach (var window in windows)
        {
            NativeMethods.ShowWindow(window.Handle, NativeMethods.SwHide);
            if (!NativeMethods.IsWindowVisible(window.Handle))
                hidden.Add(window);
        }

        return hidden;
    }

    /// <summary>Сворачивает окна на панель задач.</summary>
    public int Minimize(IEnumerable<CapturedWindow> windows)
    {
        var minimized = 0;

        foreach (var window in windows)
        {
            NativeMethods.ShowWindow(window.Handle, NativeMethods.SwMinimize);
            if (NativeMethods.IsIconic(window.Handle))
                minimized++;
        }

        return minimized;
    }

    /// <summary>Возвращает окна в прежнее состояние (обычное или развёрнутое).</summary>
    public int Restore(IEnumerable<CapturedWindow> windows)
    {
        var restored = 0;

        foreach (var window in windows)
        {
            var command = window.WasMaximized
                ? NativeMethods.SwShowMaximized
                : NativeMethods.SwRestore;

            NativeMethods.ShowWindow(window.Handle, command);

            if (NativeMethods.IsWindowVisible(window.Handle))
                restored++;
        }

        return restored;
    }

    private static void CollectWindow(
        IntPtr handle,
        uint ownProcessId,
        HashSet<string> keep,
        List<CapturedWindow> target)
    {
        if (!NativeMethods.IsWindowVisible(handle))
            return;

        // Скрытые UWP-«тени» формально видимы, но пользователь их не видит.
        if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return;
        }

        if (NativeMethods.GetWindowTextLength(handle) == 0)
            return;

        var title = ReadText(handle);
        if (string.IsNullOrWhiteSpace(title))
            return;

        if (IgnoredWindowClasses.Contains(ReadClassName(handle)))
            return;

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId == ownProcessId)
            return;

        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            // Процесс уже завершился или доступ запрещён.
            return;
        }

        if (keep.Contains(processName))
            return;

        target.Add(new CapturedWindow(
            handle,
            (int)processId,
            processName,
            title,
            NativeMethods.IsZoomed(handle)));
    }

    private static string ReadText(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
            return string.Empty;

        var builder = new StringBuilder(length + 1);
        var copied = NativeMethods.GetWindowText(handle, builder, builder.Capacity);
        return copied > 0 ? builder.ToString(0, copied) : string.Empty;
    }

    private static string ReadClassName(IntPtr handle)
    {
        var builder = new StringBuilder(256);
        return NativeMethods.GetClassName(handle, builder, builder.Capacity) > 0
            ? builder.ToString()
            : string.Empty;
    }
}
