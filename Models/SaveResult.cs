namespace ProfileOnPC.Models;

/// <summary>
/// Результат сохранения данных на диск. Ошибка ввода-вывода не выбрасывается исключением,
/// а возвращается вызывающему коду, чтобы приложение могло показать мягкое предупреждение.
/// </summary>
public sealed record SaveResult(bool Success, string? Error)
{
    public static SaveResult Ok() => new(true, null);

    public static SaveResult Fail(string error) => new(false, error);
}
