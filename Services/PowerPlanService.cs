using System.Text.RegularExpressions;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>Активная схема питания.</summary>
public sealed record PowerPlanInfo(string Guid, string Name);

/// <summary>Переключение схем питания через powercfg.exe.</summary>
public sealed partial class PowerPlanService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((?<name>[^)]*)\)")]
    private static partial Regex SchemeLinePattern();

    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex GuidPattern();

    /// <summary>Текущая схема питания или null, если powercfg недоступен.</summary>
    public async Task<PowerPlanInfo?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var active = await RunAsync("/getactivescheme", cancellationToken).ConfigureAwait(false);
        var activeGuid = FirstGuid(active.Output);
        if (activeGuid is null)
            return null;

        var list = await RunAsync("/list", cancellationToken).ConfigureAwait(false);
        var known = ParseSchemes(list.Output)
            .FirstOrDefault(scheme => scheme.Guid.Equals(activeGuid, StringComparison.OrdinalIgnoreCase));

        return new PowerPlanInfo(activeGuid, known?.Name ?? activeGuid);
    }

    /// <summary>Включает выбранную схему питания.</summary>
    public async Task<ActionResult> ApplyAsync(string planId, CancellationToken cancellationToken = default)
    {
        var plan = PowerPlans.Normalize(planId);
        if (plan == PowerPlans.Keep)
            return ActionResult.Skip("Схема питания не изменялась");

        var label = PowerPlans.GetLabel(plan);
        var guid = PowerPlans.GetGuid(plan);
        if (guid is null)
            return ActionResult.Fail($"Неизвестная схема питания: {planId}");

        if (plan == PowerPlans.Ultimate && !await ExistsAsync(guid, cancellationToken).ConfigureAwait(false))
        {
            // «Ultimate Performance» по умолчанию скрыта: создаём её копию и получаем новый GUID.
            var duplicated = await DuplicateAsync(guid, cancellationToken).ConfigureAwait(false);
            if (duplicated is not null)
                guid = duplicated;
        }

        var set = await RunAsync($"/setactive {guid}", cancellationToken).ConfigureAwait(false);
        if (!set.Success)
        {
            return ActionResult.Fail(
                $"Не удалось включить схему «{label}»: {ProcessRunner.Compact(set.Diagnostic)}");
        }

        var active = await GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return active is not null && active.Guid.Equals(guid, StringComparison.OrdinalIgnoreCase)
            ? ActionResult.Ok($"Схема питания: {label}")
            : ActionResult.Ok($"Схема «{label}» запрошена, но Windows показывает другую активную схему");
    }

    /// <summary>Включает схему питания по явному GUID (используется откатом активации).</summary>
    public async Task<ActionResult> SetActiveGuidAsync(string guid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return ActionResult.Fail("Не указан GUID схемы питания");

        var set = await RunAsync($"/setactive {guid.Trim()}", cancellationToken).ConfigureAwait(false);

        return set.Success
            ? ActionResult.Ok("Схема питания восстановлена")
            : ActionResult.Fail($"Не удалось вернуть схему питания: {ProcessRunner.Compact(set.Diagnostic)}");
    }

    private static async Task<bool> ExistsAsync(string guid, CancellationToken cancellationToken)
    {
        var list = await RunAsync("/list", cancellationToken).ConfigureAwait(false);
        return list.Output.Contains(guid, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> DuplicateAsync(string guid, CancellationToken cancellationToken)
    {
        var result = await RunAsync($"-duplicatescheme {guid}", cancellationToken).ConfigureAwait(false);
        return result.Success ? FirstGuid(result.Output) : null;
    }

    private static async Task<ProcessResult> RunAsync(string arguments, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(CommandTimeout);

        try
        {
            return await ProcessRunner
                .RunAsync("powercfg.exe", arguments, timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, "powercfg не ответил вовремя");
        }
    }

    private static string? FirstGuid(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = GuidPattern().Match(text);
        return match.Success ? match.Groups["guid"].Value : null;
    }

    private static IEnumerable<PowerPlanInfo> ParseSchemes(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            yield break;

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = SchemeLinePattern().Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value.Trim();
            yield return new PowerPlanInfo(match.Groups["guid"].Value, name.Length > 0 ? name : match.Groups["guid"].Value);
        }
    }
}
