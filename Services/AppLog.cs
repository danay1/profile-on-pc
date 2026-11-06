using System.Text;

namespace ProfileOnPC.Services;

/// <summary>
/// Простой файловый журнал приложения. Пишется в %LOCALAPPDATA%\ProfileOnPC\logs,
/// хранится 7 дней. Любая ошибка журнала игнорируется — он не должен ломать приложение.
/// </summary>
public static class AppLog
{
    private const int RetentionDays = 7;

    private static readonly object Sync = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProfileOnPC",
        "logs");

    public static string CurrentFilePath => Path.Combine(DirectoryPath, $"app-{DateTime.Now:yyyyMMdd}.log");

    static AppLog()
    {
        try
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            Cleanup();
        }
        catch
        {
            // Журнал недоступен — работаем без него.
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
            {
                line.Append(" | ")
                    .Append(exception.GetType().Name)
                    .Append(": ")
                    .Append(exception.Message);
            }

            line.AppendLine();

            lock (Sync)
            {
                System.IO.File.AppendAllText(CurrentFilePath, line.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Никогда не бросаем из журнала.
        }
    }

    private static void Cleanup()
    {
        try
        {
            var threshold = DateTime.Now.AddDays(-RetentionDays);

            foreach (var file in System.IO.Directory.EnumerateFiles(DirectoryPath, "app-*.log"))
            {
                if (System.IO.File.GetLastWriteTime(file) < threshold)
                    System.IO.File.Delete(file);
            }
        }
        catch
        {
            // Не критично.
        }
    }
}
