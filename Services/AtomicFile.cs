using System.Security;
using System.Text;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>
/// Атомарная запись небольших файлов: временный файл → сброс на диск → замена основного.
/// Ошибки не выбрасываются, а возвращаются в <see cref="SaveResult"/>.
/// </summary>
public static class AtomicFile
{
    private const int MaxAttempts = 4;

    public static async Task<SaveResult> WriteAsync(string destination, string content)
    {
        string? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string? tempPath = null;

            try
            {
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // Уникальное имя: не пересекаемся с «хвостом» прерванной записи и вторым экземпляром.
                tempPath = $"{destination}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
                await WriteFileAsync(tempPath, content).ConfigureAwait(false);

                ClearReadOnly(destination);
                ReplaceFile(tempPath, destination);

                tempPath = null;
                return SaveResult.Ok();
            }
            catch (Exception exception) when (IsFileSystemError(exception))
            {
                lastError = Describe(exception);
            }
            finally
            {
                if (tempPath is not null)
                    TryDelete(tempPath);
            }

            if (attempt < MaxAttempts)
                await Task.Delay(120 * attempt).ConfigureAwait(false);
        }

        return SaveResult.Fail(
            $"Не удалось сохранить «{destination}» за {MaxAttempts} попытки: {lastError}");
    }

    private static async Task WriteFileAsync(string path, string content)
    {
        // FileShare.None: поток закрывается до замены, поэтому дескриптор ничего не держит.
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true);

        await writer.WriteAsync(content).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        // Сбрасываем на диск до переименования: при сбое питания останется целый старый файл.
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceFile(string tempPath, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(tempPath, destination);
            return;
        }

        try
        {
            File.Replace(tempPath, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (Exception exception)
            when (exception is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // File.Replace поддерживается не на всех ФС и фильтрах антивируса.
            File.Move(tempPath, destination, overwrite: true);
        }
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Файл занят антивирусом — переживём.
        }
    }

    private static bool IsFileSystemError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            $"нет доступа. Проверьте права на папку данных и антивирус ({exception.Message})",
        _ => exception.Message
    };
}
