using System.Text.Json.Serialization;

namespace ProfileOnPC.Models;

/// <summary>
/// Состояние системы до активации профиля — чтобы активацию можно было отменить.
/// </summary>
public sealed class SystemStateSnapshot
{
    [JsonPropertyName("captured_at")]
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    [JsonPropertyName("power_plan_guid")]
    public string? PowerPlanGuid { get; set; }

    [JsonPropertyName("sysmain_start")]
    public int? SysMainStart { get; set; }

    [JsonPropertyName("sysmain_running")]
    public bool? SysMainRunning { get; set; }

    [JsonPropertyName("game_dvr")]
    public int? GameDvrEnabled { get; set; }

    [JsonPropertyName("app_capture")]
    public int? AppCaptureEnabled { get; set; }

    [JsonPropertyName("min_animate")]
    public string? MinAnimate { get; set; }

    [JsonPropertyName("visual_fx")]
    public int? VisualFxSetting { get; set; }

    [JsonIgnore]
    public bool IsEmpty => PowerPlanGuid is null
                           && SysMainStart is null
                           && GameDvrEnabled is null
                           && AppCaptureEnabled is null
                           && MinAnimate is null
                           && VisualFxSetting is null;

    /// <summary>Короткое описание для журнала.</summary>
    public override string ToString() =>
        $"power={PowerPlanGuid ?? "-"}, sysmain={SysMainStart?.ToString() ?? "-"}/{SysMainRunning?.ToString() ?? "-"}, " +
        $"gameDvr={GameDvrEnabled?.ToString() ?? "-"}, appCapture={AppCaptureEnabled?.ToString() ?? "-"}, " +
        $"minAnimate={MinAnimate ?? "-"}, visualFx={VisualFxSetting?.ToString() ?? "-"}";
}
