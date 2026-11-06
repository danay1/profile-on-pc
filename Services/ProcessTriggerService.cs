using System.Diagnostics;
using ProfileOnPC.Models;

namespace ProfileOnPC.Services;

/// <summary>
/// Авто-активация профиля по появлению нового процесса.
/// Реализовано опросом списка процессов (по умолчанию раз в 2 с): это не требует
/// пакета System.Management и не зависит от WMI, который часто отключён на игровых сборках.
/// </summary>
public sealed class ProcessTriggerService : IDisposable
{
    private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(12);

    private sealed record TriggerBinding(string ProcessName, ProfileModel Profile);

    private readonly object _sync = new();
    private readonly Dictionary<string, DateTime> _lastTriggered = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cooldown;

    private HashSet<int> _knownProcessIds = [];
    private TriggerBinding[] _bindings = [];
    private System.Threading.Timer? _timer;
    private int _polling;
    private bool _disposed;

    public ProcessTriggerService(TimeSpan? cooldown = null) => _cooldown = cooldown ?? DefaultCooldown;

    /// <summary>Срабатывает при появлении процесса из списка триггеров профиля.</summary>
    public event Action<ProfileModel, string>? Triggered;

    public void UpdateProfiles(IEnumerable<ProfileModel> profiles)
    {
        var bindings = new List<TriggerBinding>();

        foreach (var profile in profiles)
        {
            if (!profile.AutoActivateOnTrigger)
                continue;

            foreach (var name in profile.TriggerProcesses)
            {
                var normalized = ProcessPriorityService.NormalizeName(name);
                if (normalized.Length == 0)
                    continue;

                bindings.Add(new TriggerBinding(normalized, profile));
            }
        }

        lock (_sync)
            _bindings = [.. bindings];
    }

    /// <summary>Запускает мониторинг. Уже запущенные процессы не считаются триггером.</summary>
    public void Start(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(2);

        lock (_sync)
        {
            if (_disposed)
                return;

            _knownProcessIds = SnapshotProcesses().Keys.ToHashSet();

            if (_timer is null)
                _timer = new System.Threading.Timer(OnTick, null, period, period);
            else
                _timer.Change(period, period);
        }
    }

    public void Stop()
    {
        System.Threading.Timer? timer;

        lock (_sync)
        {
            timer = _timer;
            _timer = null;
        }

        if (timer is null)
            return;

        timer.Change(Timeout.Infinite, Timeout.Infinite);
        timer.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();

        lock (_sync)
            _lastTriggered.Clear();
    }

    private void OnTick(object? state)
    {
        // Тик может прийти, пока предыдущий ещё выполняется — пропускаем.
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            return;

        try
        {
            Poll();
        }
        catch
        {
            // Мониторинг не должен падать из-за недоступного процесса.
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void Poll()
    {
        var current = SnapshotProcesses();
        if (current.Count == 0)
            return;

        List<(int ProcessId, string Name)> started = [];

        lock (_sync)
        {
            foreach (var pair in current)
            {
                if (_knownProcessIds.Add(pair.Key))
                    started.Add((pair.Key, pair.Value));
            }

            // Убираем завершившиеся процессы, чтобы набор не рос бесконечно.
            _knownProcessIds.RemoveWhere(id => !current.ContainsKey(id));
        }

        if (started.Count == 0)
            return;

        TriggerBinding[] bindings;
        lock (_sync)
            bindings = _bindings;

        if (bindings.Length == 0)
            return;

        foreach (var (_, name) in started)
        {
            foreach (var binding in bindings)
            {
                if (!binding.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryEnterCooldown(binding.Profile))
                    continue;

                try
                {
                    Triggered?.Invoke(binding.Profile, name);
                }
                catch
                {
                    // Обработчик вызывается на UI-потоке приложения через Dispatcher.
                }
            }
        }
    }

    private bool TryEnterCooldown(ProfileModel profile)
    {
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            if (_lastTriggered.TryGetValue(profile.Id, out var last) && now - last < _cooldown)
                return false;

            _lastTriggered[profile.Id] = now;
            return true;
        }
    }

    private static Dictionary<int, string> SnapshotProcesses()
    {
        var result = new Dictionary<int, string>();
        var ownProcessId = Environment.ProcessId;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return result;
        }

        foreach (var process in processes)
        {
            try
            {
                var id = process.Id;
                if (id != ownProcessId)
                    result[id] = process.ProcessName;
            }
            catch
            {
                // Завершившийся или защищённый процесс — пропускаем.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }
}
