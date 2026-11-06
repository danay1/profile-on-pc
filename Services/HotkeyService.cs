using System.Windows;
using System.Windows.Interop;
using ProfileOnPC.Services.Native;

namespace ProfileOnPC.Services;

/// <summary>Итог регистрации набора горячих клавиш.</summary>
public sealed record HotkeyRegistrationResult(
    IReadOnlyList<string> Registered,
    IReadOnlyList<string> Failed);

/// <summary>
/// Глобальные горячие клавиши через RegisterHotKey + оконная процедура WPF.
/// Комбинации работают, пока приложение запущено (в том числе из трея).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly List<int> _registeredIds = new();
    private HwndSource? _source;
    private IntPtr _handle = IntPtr.Zero;
    private int _nextId = 0x5000;
    private bool _disposed;

    /// <summary>Подключается к окну; повторные вызовы безопасны.</summary>
    public bool Initialize(Window window)
    {
        if (_disposed)
            return false;

        if (_source is not null)
            return true;

        var helper = new WindowInteropHelper(window);
        _handle = helper.Handle;
        if (_handle == IntPtr.Zero)
            return false;

        _source = HwndSource.FromHwnd(_handle);
        if (_source is null)
            return false;

        _source.AddHook(WndProc);
        return true;
    }

    /// <summary>Перерегистрирует весь набор комбинаций. Возвращает список успехов и конфликтов.</summary>
    public HotkeyRegistrationResult Rebuild(IEnumerable<(string Gesture, Action Callback)> bindings)
    {
        UnregisterAll();

        var registered = new List<string>();
        var failed = new List<string>();

        if (_source is null || _handle == IntPtr.Zero)
        {
            failed.Add("окно приложения ещё не готово");
            return new HotkeyRegistrationResult(registered, failed);
        }

        foreach (var (gesture, callback) in bindings)
        {
            if (!HotkeyGesture.TryParse(gesture, out var parsed, out var error))
            {
                failed.Add($"{gesture} — {error}");
                continue;
            }

            var id = _nextId++;
            if (!NativeMethods.RegisterHotKey(
                    _handle,
                    id,
                    parsed.Modifiers | NativeMethods.ModNoRepeat,
                    parsed.VirtualKey))
            {
                // ERROR_HOTKEY_ALREADY_REGISTERED = 1409 — комбинацию занял кто-то ещё.
                failed.Add($"{parsed.Display} — уже занята другой программой");
                continue;
            }

            _callbacks[id] = callback;
            _registeredIds.Add(id);
            registered.Add(parsed.Display);
        }

        return new HotkeyRegistrationResult(registered, failed);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnregisterAll();

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        _handle = IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && _callbacks.TryGetValue(wParam.ToInt32(), out var callback))
        {
            handled = true;

            try
            {
                callback();
            }
            catch
            {
                // Ошибка обработчика не должна ронять поток сообщений окна.
            }
        }

        return IntPtr.Zero;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registeredIds)
        {
            if (_handle != IntPtr.Zero)
                NativeMethods.UnregisterHotKey(_handle, id);
        }

        _registeredIds.Clear();
        _callbacks.Clear();
    }
}
