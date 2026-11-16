using QuickRewrite.Interop;

namespace QuickRewrite.Services;

public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayName)
{
    public static bool TryParse(string? value, out HotkeyGesture? gesture, out string error)
    {
        gesture = null;
        error = "";

        if (string.IsNullOrWhiteSpace(value))
        {
            error = "快捷键不能为空。";
            return false;
        }

        var tokens = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2)
        {
            error = "快捷键至少需要一个修饰键和一个主键，例如 Ctrl+Alt+R。";
            return false;
        }

        uint modifiers = 0;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            switch (tokens[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;
                default:
                    error = $"无法识别修饰键：{tokens[i]}";
                    return false;
            }
        }

        if (modifiers == 0)
        {
            error = "快捷键必须包含 Ctrl、Alt、Shift 或 Win。";
            return false;
        }

        var keyToken = tokens[^1].ToUpperInvariant();
        if (!TryGetVirtualKey(keyToken, out var virtualKey, out var displayKey))
        {
            error = $"无法识别主键：{tokens[^1]}";
            return false;
        }

        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(displayKey);

        gesture = new HotkeyGesture(modifiers, virtualKey, string.Join("+", parts));
        return true;
    }

    private static bool TryGetVirtualKey(string token, out uint virtualKey, out string display)
    {
        virtualKey = 0;
        display = token;

        if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0]))
        {
            virtualKey = char.ToUpperInvariant(token[0]);
            return true;
        }

        if (token.StartsWith('F') && int.TryParse(token[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        var namedKeys = new Dictionary<string, (uint Key, string Display)>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = (0x20, "Space"),
            ["ENTER"] = (0x0D, "Enter"),
            ["TAB"] = (0x09, "Tab"),
            ["ESC"] = (0x1B, "Esc"),
            ["ESCAPE"] = (0x1B, "Esc"),
            ["BACKSPACE"] = (0x08, "Backspace"),
            ["DELETE"] = (0x2E, "Delete"),
            ["INSERT"] = (0x2D, "Insert"),
            ["HOME"] = (0x24, "Home"),
            ["END"] = (0x23, "End"),
            ["PAGEUP"] = (0x21, "PageUp"),
            ["PAGEDOWN"] = (0x22, "PageDown"),
            ["UP"] = (0x26, "Up"),
            ["DOWN"] = (0x28, "Down"),
            ["LEFT"] = (0x25, "Left"),
            ["RIGHT"] = (0x27, "Right")
        };

        if (namedKeys.TryGetValue(token, out var named))
        {
            virtualKey = named.Key;
            display = named.Display;
            return true;
        }

        return false;
    }
}