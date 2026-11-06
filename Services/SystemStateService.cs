using System.Security;
using Microsoft.Win32;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>
/// Снимок состояния системы до активации профиля и его восстановление.
/// Покрывает то, что профиль меняет необратимо: схема питания, SysMain, Game DVR, анимации.
/// </summary>
public sealed class SystemStateService
{
    private const string SysMainKeyPath = @"SYSTEM\CurrentControlSet\Services\SysMain";
    private const string GameConfigPath = @"System\GameConfigStore";
    private const string GameDvrPath = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string DesktopPath = @"Control Panel\Desktop";
    private const string VisualEffectsPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";

    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(15);

    private readonly SystemServiceManager _services = new();
    private readonly PowerPlanService _power = new();

    public async Task<SystemStateSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _power.GetActiveAsync(cancellationToken).ConfigureAwait(false);

        return new SystemStateSnapshot
        {
            PowerPlanGuid = plan?.Guid,
            SysMainStart = ReadDword(Registry.LocalMachine, SysMainKeyPath, "Start"),
            SysMainRunning = _services.QueryState(SysMainModes.ServiceName) switch
            {
                SystemServiceState.Running => true,
                SystemServiceState.Stopped => false,
                _ => null
            },
            GameDvrEnabled = ReadDword(Registry.CurrentUser, GameConfigPath, "GameDVR_Enabled"),
            AppCaptureEnabled = ReadDword(Registry.CurrentUser, GameDvrPath, "AppCaptureEnabled"),
            MinAnimate = ReadString(Registry.CurrentUser, DesktopPath, "MinAnimate"),
            VisualFxSetting = ReadDword(Registry.CurrentUser, VisualEffectsPath, "VisualFXSetting")
        };
    }

    public async Task<IReadOnlyList<ActionResult>> RestoreAsync(
        SystemStateSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ActionResult>();

        if (!string.IsNullOrWhiteSpace(snapshot.PowerPlanGuid))
        {
            results.Add(await _power
                .SetActiveGuidAsync(snapshot.PowerPlanGuid!, cancellationToken)
                .ConfigureAwait(false));
        }

        if (snapshot.SysMainStart is { } startType)
        {
            results.Add(WriteDword(Registry.LocalMachine, SysMainKeyPath, "Start", startType, "тип запуска SysMain"));

            if (snapshot.SysMainRunning == true)
            {
                results.Add(await _services
                    .StartAsync(SysMainModes.ServiceName, ServiceTimeout, cancellationToken)
                    .ConfigureAwait(false));
            }
            else if (snapshot.SysMainRunning == false)
            {
                results.Add(await _services
                    .StopAsync(SysMainModes.ServiceName, ServiceTimeout, cancellationToken)
                    .ConfigureAwait(false));
            }
        }

        if (snapshot.GameDvrEnabled is { } gameDvr)
            results.Add(WriteDword(Registry.CurrentUser, GameConfigPath, "GameDVR_Enabled", gameDvr, "Game DVR"));

        if (snapshot.AppCaptureEnabled is { } appCapture)
            results.Add(WriteDword(Registry.CurrentUser, GameDvrPath, "AppCaptureEnabled", appCapture, "запись Game Bar"));

        if (snapshot.MinAnimate is not null)
            results.Add(WriteString(Registry.CurrentUser, DesktopPath, "MinAnimate", snapshot.MinAnimate));

        if (snapshot.VisualFxSetting is { } visualFx)
        {
            results.Add(WriteDword(
                Registry.CurrentUser,
                VisualEffectsPath,
                "VisualFXSetting",
                visualFx,
                "визуальные эффекты"));
        }

        return results;
    }

    private static int? ReadDword(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path, writable: false);
            var value = key?.GetValue(name);
            return value is null ? null : Convert.ToInt32(value);
        }
        catch (Exception exception) when (IsRegistryError(exception))
        {
            return null;
        }
    }

    private static string? ReadString(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path, writable: false);
            return key?.GetValue(name) as string;
        }
        catch (Exception exception) when (IsRegistryError(exception))
        {
            return null;
        }
    }

    private static ActionResult WriteDword(RegistryKey root, string path, string name, int value, string label)
    {
        try
        {
            using var key = root.CreateSubKey(path, writable: true);
            if (key is null)
                return ActionResult.Fail($"Не удалось открыть раздел реестра для «{label}»");

            key.SetValue(name, value, RegistryValueKind.DWord);
            return ActionResult.Ok($"{label}: восстановлено ({value})");
        }
        catch (UnauthorizedAccessException exception)
        {
            return ActionResult.Fail($"Нет прав на восстановление «{label}»: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or SecurityException)
        {
            return ActionResult.Fail($"Не удалось восстановить «{label}»: {exception.Message}");
        }
    }

    private static ActionResult WriteString(RegistryKey root, string path, string name, string value)
    {
        try
        {
            using var key = root.CreateSubKey(path, writable: true);
            if (key is null)
                return ActionResult.Fail("Не удалось открыть раздел реестра для «MinAnimate»");

            key.SetValue(name, value, RegistryValueKind.String);
            return ActionResult.Ok("анимации: восстановлено исходное значение");
        }
        catch (Exception exception) when (IsRegistryError(exception))
        {
            return ActionResult.Fail($"Не удалось восстановить «MinAnimate»: {exception.Message}");
        }
    }

    private static bool IsRegistryError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or FormatException;
}
