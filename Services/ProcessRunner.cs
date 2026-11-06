using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ProfileOnPC.Services;

/// <summary>Результат запуска внешней утилиты (powercfg, ipconfig и т. п.).</summary>
public sealed record ProcessResult(bool Started, int ExitCode, string Output, string Error, string? FailureReason)
{
    public bool Success => Started && ExitCode == 0;

    public string Diagnostic => !Started
        ? FailureReason ?? "процесс не запустился"
        : !string.IsNullOrWhiteSpace(Error)
            ? Error.Trim()
            : Output.Trim();
}

/// <summary>Запуск консольных утилит Windows с перехватом вывода и отменой.</summary>
public static class ProcessRunner
{
    static ProcessRunner()
    {
        // Без провайдера .NET не умеет читать OEM-кодировку консоли (например CP866).
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            // Не критично: останется UTF-8 по умолчанию.
        }
    }

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var encoding = ConsoleEncoding();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return new ProcessResult(false, -1, string.Empty, string.Empty, $"не удалось запустить {fileName}");

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new ProcessResult(true, process.ExitCode, output, error, null);
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, exception.Message);
        }
    }

    /// <summary>Схлопывает многострочный вывод в одну строку для сообщений пользователю.</summary>
    public static string Compact(string? value, int maxLength = 180)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var singleLine = string.Join(
            ' ',
            value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "…";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            // Процесс уже завершился или недоступен — отмена важнее.
        }
    }

    /// <summary>
    /// Консольные утилиты Windows пишут в OEM-кодировке, поэтому читаем вывод именно ей,
    /// иначе русские названия схем питания и сообщения служб превращаются в кракозябры.
    /// </summary>
    private static Encoding ConsoleEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
