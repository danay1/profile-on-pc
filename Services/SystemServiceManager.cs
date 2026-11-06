using System.ComponentModel;
using System.Runtime.InteropServices;
using ProfileOnPC.Models;
using ProfileOnPC.Services.Native;

namespace ProfileOnPC.Services;

public enum SystemServiceState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    ContinuePending = 5,
    PausePending = 6,
    Paused = 7
}

/// <summary>
/// Управление службами Windows через SCM API (advapi32), без пакета
/// System.ServiceProcess.ServiceController.
/// </summary>
public sealed class SystemServiceManager
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceAlreadyRunning = 1056;

    public SystemServiceState QueryState(string serviceName)
    {
        var handle = OpenServiceHandle(serviceName, NativeMethods.ServiceQueryStatus, out _);
        if (handle == IntPtr.Zero)
            return SystemServiceState.Unknown;

        try
        {
            return QueryState(handle);
        }
        finally
        {
            NativeMethods.CloseServiceHandle(handle);
        }
    }

    public ActionResult SetStartup(string serviceName, bool automatic)
    {
        var handle = OpenServiceHandle(serviceName, NativeMethods.ServiceChangeConfig, out var error);
        if (handle == IntPtr.Zero)
            return ActionResult.Fail(DescribeAccess(serviceName, error));

        try
        {
            var startType = automatic
                ? NativeMethods.ServiceAutoStart
                : NativeMethods.ServiceDisabled;

            var changed = NativeMethods.ChangeServiceConfig(
                handle,
                NativeMethods.ServiceNoChange,
                startType,
                NativeMethods.ServiceNoChange,
                null,
                null,
                IntPtr.Zero,
                null,
                null,
                null,
                null);

            if (!changed)
                return ActionResult.Fail(
                    $"Не удалось изменить тип запуска службы «{serviceName}»: {Describe(LastError())}");

            return ActionResult.Ok(automatic
                ? $"Служба «{serviceName}»: автоматический запуск"
                : $"Служба «{serviceName}»: запуск запрещён");
        }
        finally
        {
            NativeMethods.CloseServiceHandle(handle);
        }
    }

    public async Task<ActionResult> StopAsync(
        string serviceName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var handle = OpenServiceHandle(
            serviceName,
            NativeMethods.ServiceStop | NativeMethods.ServiceQueryStatus,
            out var error);

        if (handle == IntPtr.Zero)
            return ActionResult.Fail(DescribeAccess(serviceName, error));

        try
        {
            if (QueryState(handle) == SystemServiceState.Stopped)
                return ActionResult.Skip($"Служба «{serviceName}» уже остановлена");

            var status = default(NativeMethods.ServiceStatusProcess);
            if (!NativeMethods.ControlService(handle, NativeMethods.ServiceControlStop, ref status))
            {
                var code = LastError();
                if (code == ErrorServiceNotActive)
                    return ActionResult.Skip($"Служба «{serviceName}» уже остановлена");

                return ActionResult.Fail($"Не удалось остановить службу «{serviceName}»: {Describe(code)}");
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(handle);
        }

        var stopped = await WaitForStateAsync(
                serviceName,
                SystemServiceState.Stopped,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        return stopped
            ? ActionResult.Ok($"Служба «{serviceName}» остановлена")
            : ActionResult.Fail($"Служба «{serviceName}» не остановилась за {timeout.TotalSeconds:0} с");
    }

    public async Task<ActionResult> StartAsync(
        string serviceName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var handle = OpenServiceHandle(
            serviceName,
            NativeMethods.ServiceStart | NativeMethods.ServiceQueryStatus,
            out var error);

        if (handle == IntPtr.Zero)
            return ActionResult.Fail(DescribeAccess(serviceName, error));

        try
        {
            if (QueryState(handle) == SystemServiceState.Running)
                return ActionResult.Skip($"Служба «{serviceName}» уже работает");

            if (!NativeMethods.StartService(handle, 0, IntPtr.Zero))
            {
                var code = LastError();
                if (code == ErrorServiceAlreadyRunning)
                    return ActionResult.Skip($"Служба «{serviceName}» уже работает");

                return ActionResult.Fail($"Не удалось запустить службу «{serviceName}»: {Describe(code)}");
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(handle);
        }

        var running = await WaitForStateAsync(
                serviceName,
                SystemServiceState.Running,
                timeout,
                cancellationToken)
            .ConfigureAwait(false);

        return running
            ? ActionResult.Ok($"Служба «{serviceName}» запущена")
            : ActionResult.Fail($"Служба «{serviceName}» не запустилась за {timeout.TotalSeconds:0} с");
    }

    private async Task<bool> WaitForStateAsync(
        string serviceName,
        SystemServiceState expected,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (QueryState(serviceName) == expected)
                return true;

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return QueryState(serviceName) == expected;
    }

    private static SystemServiceState QueryState(IntPtr service)
    {
        var status = default(NativeMethods.ServiceStatusProcess);
        var size = Marshal.SizeOf<NativeMethods.ServiceStatusProcess>();

        if (!NativeMethods.QueryServiceStatusEx(
                service,
                NativeMethods.ScStatusProcessInfo,
                ref status,
                size,
                out _))
        {
            return SystemServiceState.Unknown;
        }

        return status.CurrentState <= (uint)SystemServiceState.Paused
            ? (SystemServiceState)status.CurrentState
            : SystemServiceState.Unknown;
    }

    private static IntPtr OpenServiceHandle(string serviceName, uint access, out int error)
    {
        error = 0;

        var manager = NativeMethods.OpenSCManager(null, null, NativeMethods.ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            error = LastError();
            return IntPtr.Zero;
        }

        try
        {
            var service = NativeMethods.OpenService(manager, serviceName, access);
            if (service == IntPtr.Zero)
                error = LastError();

            return service;
        }
        finally
        {
            // Закрытие менеджера не делает недействительным хэндл самой службы.
            NativeMethods.CloseServiceHandle(manager);
        }
    }

    private static int LastError() => Marshal.GetLastWin32Error();

    private static string DescribeAccess(string serviceName, int errorCode) => errorCode switch
    {
        ErrorServiceDoesNotExist => $"Служба «{serviceName}» не установлена в этой версии Windows",
        ErrorAccessDenied => $"Нет прав на управление службой «{serviceName}» — запустите приложение от администратора",
        _ => $"Не удалось открыть службу «{serviceName}»: {Describe(errorCode)}"
    };

    private static string Describe(int errorCode)
    {
        if (errorCode == 0)
            return "неизвестная ошибка";

        var message = new Win32Exception(errorCode).Message;
        return string.IsNullOrWhiteSpace(message) ? $"код {errorCode}" : $"{message} ({errorCode})";
    }
}
