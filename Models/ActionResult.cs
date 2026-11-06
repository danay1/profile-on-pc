namespace ProfileOnPC.Models;

/// <summary>
/// Результат одного шага активации профиля (очистка, запуск процесса, смена схемы питания и т. п.).
/// </summary>
/// <param name="Success">Шаг завершился без ошибки.</param>
/// <param name="Skipped">Шаг не требовался: действие уже выполнено или неприменимо.</param>
/// <param name="Message">Текст для журнала активации.</param>
public sealed record ActionResult(bool Success, bool Skipped, string Message)
{
    public static ActionResult Ok(string message) => new(true, false, message);

    public static ActionResult Skip(string message) => new(true, true, message);

    public static ActionResult Fail(string message) => new(false, false, message);
}
