using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>
/// Хранение профилей и настроек в %LOCALAPPDATA%\ProfileOnPC.
/// Запись атомарная: временный файл → сброс на диск → замена основного файла,
/// с ретраями на случай блокировки антивирусом. Ошибки ввода-вывода не выбрасываются,
/// а возвращаются в <see cref="SaveResult"/>.
/// </summary>
public sealed class StorageService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        // Устаревшие поля (например high_performance_power) не должны попадать в файл.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string DataDirectory { get; }

    /// <summary>
    /// Можно ли писать в папку данных. Проверяется пробной записью при запуске: в песочнице
    /// или AppContainer каталог существует и читается, но создавать в нём файлы запрещено.
    /// </summary>
    public bool IsWritable { get; }

    /// <summary>Причина, по которой папка недоступна для записи.</summary>
    public string? WriteProbeError { get; private set; }

    /// <summary>
    /// Причина последней неудачной загрузки. Если данных «нет» из-за прав на папку,
    /// интерфейс должен предупредить, а не молча показать пустой список профилей.
    /// </summary>
    public string? LastLoadError { get; private set; }

    private string ProfilesPath => Path.Combine(DataDirectory, "profiles.json");
    private string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public StorageService()
    {
        DataDirectory = ResolveDataDirectory();

        try
        {
            // Папка данных создаётся заранее и повторно — перед каждой записью.
            Directory.CreateDirectory(DataDirectory);
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            WriteProbeError = Describe(exception);
        }

        IsWritable = TryProbeWritable(DataDirectory, out var probeError);
        if (!IsWritable)
            WriteProbeError ??= probeError;
    }

    private static string ResolveDataDirectory()
    {
        // Аварийный выход для песочниц и портативного использования:
        // PROFILEONPC_DATA_DIR=путь, куда процессу действительно разрешено писать.
        var custom = Environment.GetEnvironmentVariable("PROFILEONPC_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            try
            {
                return Path.GetFullPath(custom.Trim().Trim('"'));
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Некорректный путь — работаем со стандартным.
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProfileOnPC");
    }

    private static bool TryProbeWritable(string directory, out string? error)
    {
        error = null;

        try
        {
            Directory.CreateDirectory(directory);

            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.DeleteOnClose);

            stream.WriteByte(0);
            return true;
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            error = Describe(exception);
            return false;
        }
    }

    // ------------------------------------------------------------------ чтение

    public List<ProfileModel> LoadProfiles()
    {
        try
        {
            if (!File.Exists(ProfilesPath))
                return [];

            var json = File.ReadAllText(ProfilesPath);
            var profiles = JsonSerializer.Deserialize<List<ProfileModel>>(json, _jsonOptions) ?? [];

            foreach (var profile in profiles)
                profile.Normalize();

            return profiles;
        }
        catch (Exception exception)
        {
            LastLoadError = $"Не удалось прочитать профили «{ProfilesPath}»: {exception.Message}";
            return [];
        }
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(SettingsPath),
                _jsonOptions) ?? new AppSettings();

            NormalizeSettings(settings);
            return settings;
        }
        catch (Exception exception)
        {
            LastLoadError = $"Не удалось прочитать настройки «{SettingsPath}»: {exception.Message}";
            return new AppSettings();
        }
    }

    // ------------------------------------------------------------------ запись

    public async Task<SaveResult> SaveProfilesAsync(IEnumerable<ProfileModel> profiles)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(profiles, _jsonOptions);
        }
        catch (Exception exception)
        {
            return SaveResult.Fail($"Не удалось подготовить профили к записи: {exception.Message}");
        }

        return await WriteAtomicAsync(ProfilesPath, json).ConfigureAwait(false);
    }

    public async Task<SaveResult> SaveSettingsAsync(AppSettings settings)
    {
        string json;
        try
        {
            json = JsonSerializer.Serialize(settings, _jsonOptions);
        }
        catch (Exception exception)
        {
            return SaveResult.Fail($"Не удалось подготовить настройки к записи: {exception.Message}");
        }

        return await WriteAtomicAsync(SettingsPath, json).ConfigureAwait(false);
    }

    private static Task<SaveResult> WriteAtomicAsync(string destination, string content) =>
        AtomicFile.WriteAsync(destination, content);

    private static bool IsFileSystemError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException;

    private static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException =>
            $"нет доступа. Проверьте права на папку данных и антивирус ({exception.Message})",
        _ => exception.Message
    };

    private static void NormalizeSettings(AppSettings settings)
    {
        settings.Theme = settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";

        settings.AccentColor = settings.AccentColor.Trim().ToLowerInvariant() switch
        {
            "blue" => "#0078D4",
            "purple" or "#8b5cf6" or "#4cc2ff" => "#2563A6",
            "green" or "red" or "orange" => "#64748B",
            var value when value.StartsWith('#') => settings.AccentColor,
            _ => "#2563A6"
        };
    }
}
