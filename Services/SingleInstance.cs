using System.Threading;

namespace ProfileOnPC.Services;

/// <summary>
/// Один экземпляр приложения: повторный запуск не создаёт второй процесс,
/// а показывает окно уже работающего. Это же служит «аварийным выходом»,
/// если иконка в трее скрыта в области переполнения.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ProfileOnPC.SingleInstance";
    private const string ShowEventName = @"Local\ProfileOnPC.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private Thread? _listener;
    private volatile bool _stopped;
    private bool _disposed;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;

        _showEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            ShowEventName);

        if (IsFirstInstance)
            StartListener();
    }

    /// <summary>true — этот процесс первый и владеет приложением.</summary>
    public bool IsFirstInstance { get; }

    /// <summary>Запрос показать окно (приходит из второго экземпляра, на фоновом потоке).</summary>
    public event Action? ShowRequested;

    /// <summary>Просит работающий экземпляр показать окно.</summary>
    public void SignalExistingInstance()
    {
        try
        {
            _showEvent.Set();
        }
        catch (Exception exception)
        {
            AppLog.Warn("Не удалось передать сигнал работающему экземпляру", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stopped = true;

        try
        {
            _showEvent.Set();   // разбудить слушатель, чтобы он завершился
        }
        catch
        {
            // ignore
        }

        _listener?.Join(TimeSpan.FromSeconds(1));
        _showEvent.Dispose();

        if (IsFirstInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Мьютекс уже не принадлежал нам.
            }
        }

        _mutex.Dispose();
    }

    private void StartListener()
    {
        _listener = new Thread(Listen)
        {
            IsBackground = true,
            Name = "ProfileOnPC.ShowWindowListener"
        };

        _listener.Start();
    }

    private void Listen()
    {
        while (!_stopped)
        {
            try
            {
                if (!_showEvent.WaitOne(TimeSpan.FromMilliseconds(500)))
                    continue;

                if (_stopped)
                    return;

                ShowRequested?.Invoke();
            }
            catch (Exception exception)
            {
                AppLog.Warn("Слушатель второго запуска остановлен", exception);
                return;
            }
        }
    }
}
