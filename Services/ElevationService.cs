using System.ComponentModel;
using System.Diagnostics;
using ProfileOnPC.Services.Native;

namespace ProfileOnPC.Services;

/// <summary>Проверка и повышение прав, а также включение привилегий процесса.</summary>
public static class ElevationService
{
    private static readonly Lazy<bool> LazyIsAdministrator = new(ComputeIsAdministrator);

    /// <summary>true, если текущий процесс запущен с повышенными правами (UAC).</summary>
    public static bool IsAdministrator => LazyIsAdministrator.Value;

    /// <summary>Включает привилегию текущего процесса, например SeProfileSingleProcessPrivilege.</summary>
    public static bool TryEnablePrivilege(string privilegeName, out string error)
    {
        error = string.Empty;

        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TokenAdjustPrivileges | NativeMethods.TokenQuery,
                out var token))
        {
            error = Describe(MarshalLastError());
            return false;
        }

        try
        {
            if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out var luid))
            {
                error = Describe(MarshalLastError());
                return false;
            }

            var state = new NativeMethods.TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SePrivilegeEnabled
            };

            if (!NativeMethods.AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero))
            {
                error = Describe(MarshalLastError());
                return false;
            }

            // AdjustTokenPrivileges возвращает true, даже если привилегия не назначена.
            var lastError = MarshalLastError();
            if (lastError == NativeMethods.ErrorNotAllAssigned)
            {
                error = $"привилегия «{privilegeName}» не назначена текущему пользователю";
                return false;
            }

            return true;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>Перезапускает приложение с запросом UAC. false — пользователь отказался или запуск не удался.</summary>
    public static bool TryRelaunchAsAdministrator(out string error)
    {
        error = string.Empty;

        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                error = "не удалось определить путь к приложению";
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            // Для запуска через «dotnet ProfileOnPC.dll» нужно передать сборку аргументом.
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                startInfo.Arguments = $"\"{Path.Combine(AppContext.BaseDirectory, "ProfileOnPC.dll")}\"";

            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            error = "запуск отменён пользователем";
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool ComputeIsAdministrator()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TokenQuery,
                out var token))
        {
            return false;
        }

        try
        {
            return NativeMethods.GetTokenInformation(
                       token,
                       NativeMethods.TokenElevation,
                       out var elevation,
                       sizeof(int),
                       out _)
                   && elevation != 0;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>
    /// Уровень целостности процесса: Low / Medium / High / System.
    /// Процесс с Low integrity не может писать в профиль пользователя (Medium),
    /// даже если ACL папки разрешает запись его учётной записи.
    /// </summary>
    public static string GetIntegrityLevelName()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TokenQuery,
                out var token))
        {
            return "неизвестно";
        }

        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(64);

        try
        {
            if (!NativeMethods.GetTokenInformation(
                    token,
                    NativeMethods.TokenIntegrityLevel,
                    buffer,
                    64,
                    out _))
            {
                return "неизвестно";
            }

            var sid = System.Runtime.InteropServices.Marshal.ReadIntPtr(buffer);
            if (!NativeMethods.ConvertSidToStringSid(sid, out var stringSid))
                return "неизвестно";

            try
            {
                var value = System.Runtime.InteropServices.Marshal.PtrToStringUni(stringSid) ?? string.Empty;
                return value switch
                {
                    "S-1-16-0" => "Untrusted",
                    "S-1-16-4096" => "Low",
                    "S-1-16-8192" => "Medium",
                    "S-1-16-8448" => "MediumPlus",
                    "S-1-16-12288" => "High",
                    "S-1-16-16384" => "System",
                    _ => value
                };
            }
            finally
            {
                NativeMethods.LocalFree(stringSid);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            NativeMethods.CloseHandle(token);
        }
    }

    /// <summary>
    /// Краткое описание контекста безопасности процесса — нужно в диагностике отказов доступа:
    /// песочницы и AppContainer запускают приложение с урезанным токеном, и запись в
    /// %LOCALAPPDATA% запрещается, хотя ACL папки разрешает её обычному пользователю.
    /// </summary>
    public static string DescribeSecurityContext()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TokenQuery,
                out var token))
        {
            return "контекст безопасности недоступен";
        }

        try
        {
            var elevated = NativeMethods.GetTokenInformation(
                               token, NativeMethods.TokenElevation, out var elevation, sizeof(int), out _)
                           && elevation != 0;

            var appContainer = NativeMethods.GetTokenInformation(
                                   token, NativeMethods.TokenIsAppContainer, out var container, sizeof(int), out _)
                               && container != 0;

            // Для TokenRestrictedSids нужен буфер под TOKEN_GROUPS, а не 4 байта —
            // иначе GetTokenInformation возвращает ERROR_INSUFFICIENT_BUFFER.
            var restrictedSids = -1;
            var groupsBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(2048);
            try
            {
                if (NativeMethods.GetTokenInformation(
                        token, NativeMethods.TokenRestrictedSidCount, groupsBuffer, 2048, out _))
                {
                    restrictedSids = System.Runtime.InteropServices.Marshal.ReadInt32(groupsBuffer);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(groupsBuffer);
            }

            return $"повышенные права={elevated}, AppContainer={appContainer}, " +
                   $"ограниченных SID={restrictedSids}, целостность={GetIntegrityLevelName()}";
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static int MarshalLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();

    private static string Describe(int errorCode) => errorCode == 0
        ? "неизвестная ошибка Win32"
        : $"{new Win32Exception(errorCode).Message} ({errorCode})";
}
