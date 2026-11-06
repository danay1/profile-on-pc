using System.Windows.Input;

namespace ProfileOnPC.Services;

/// <summary>Разобранная горячая клавиша: модификаторы Win32, виртуальный код и текст для интерфейса.</summary>
public readonly record struct HotkeyBinding(uint Modifiers, uint VirtualKey, string Display);

/// <summary>Разбор и форматирование комбинаций вида «Ctrl+Alt+1».</summary>
public static class HotkeyGesture
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Формирует комбинацию по нажатым клавишам. null — комбинация недопустима.</summary>
    public static string? FromKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierKey(key))
            return null;

        var flags = 0u;
        if (modifiers.HasFlag(ModifierKeys.Control))
            flags |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            flags |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            flags |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows))
            flags |= ModWin;

        // Одиночные клавиши не перехватываем: это ломает обычную работу в Windows.
        if ((flags & (ModControl | ModAlt | ModWin)) == 0)
            return null;

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey == 0 ? null : Format(flags, virtualKey);
    }

    public static bool TryParse(string? gesture, out HotkeyBinding binding, out string error)
    {
        binding = default;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "комбинация не задана";
            return false;
        }

        var modifiers = 0u;
        Key? key = null;

        var tokens = gesture.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            error = "комбинация не задана";
            return false;
        }

        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= ModWin;
                    break;
                default:
                    if (key is not null)
                    {
                        error = $"лишняя клавиша «{token}»";
                        return false;
                    }

                    if (!TryParseKey(token, out var parsedKey))
                    {
                        error = $"неизвестная клавиша «{token}»";
                        return false;
                    }

                    key = parsedKey;
                    break;
            }
        }

        if (key is null)
        {
            error = "не указана клавиша";
            return false;
        }

        if ((modifiers & (ModControl | ModAlt | ModWin)) == 0)
        {
            error = "нужен модификатор Ctrl, Alt или Win";
            return false;
        }

        if (IsModifierKey(key.Value))
        {
            error = "нужна обычная клавиша вместе с модификатором";
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key.Value);
        if (virtualKey == 0)
        {
            error = "клавиша не поддерживается";
            return false;
        }

        binding = new HotkeyBinding(modifiers, virtualKey, Format(modifiers, virtualKey));
        return true;
    }

    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>(4);

        if ((modifiers & ModControl) != 0)
            parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0)
            parts.Add("Alt");
        if ((modifiers & ModShift) != 0)
            parts.Add("Shift");
        if ((modifiers & ModWin) != 0)
            parts.Add("Win");

        parts.Add(FormatKey(KeyInterop.KeyFromVirtualKey((int)virtualKey)));
        return string.Join("+", parts);
    }

    private static bool TryParseKey(string token, out Key key)
    {
        key = Key.None;
        var value = token.Trim();
        if (value.Length == 0)
            return false;

        if (value.Length == 1)
        {
            var character = char.ToUpperInvariant(value[0]);
            if (character is >= 'A' and <= 'Z')
                return Enum.TryParse(character.ToString(), out key);

            // В WPF цифры верхнего ряда называются D0…D9.
            if (character is >= '0' and <= '9')
                return Enum.TryParse("D" + character, out key);
        }

        return Enum.TryParse(value, true, out key) && key != Key.None;
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt
        or Key.LWin or Key.RWin
        or Key.System or Key.None;

    private static string FormatKey(Key key)
    {
        var value = (int)key;

        if (value >= (int)Key.D0 && value <= (int)Key.D9)
            return (value - (int)Key.D0).ToString();

        if (value >= (int)Key.A && value <= (int)Key.Z)
            return ((char)('A' + (value - (int)Key.A))).ToString();

        return key == Key.None ? "?" : key.ToString();
    }
}
