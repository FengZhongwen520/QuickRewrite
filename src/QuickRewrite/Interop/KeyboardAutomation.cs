using System.Globalization;
using static QuickRewrite.Interop.NativeMethods;

namespace QuickRewrite.Interop;

internal static class KeyboardAutomation
{
    public static bool SendCopy() => SendCtrlKey(VK_C);
    public static bool SendPaste() => SendCtrlKey(VK_V);
    public static bool SendUndo() => SendCtrlKey(VK_Z);

    public static async Task<bool> SelectPreviousTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var characterCount = StringInfo.ParseCombiningCharacters(text).Length;
        if (characterCount <= 0)
        {
            return true;
        }

        try
        {
            if (!SendInputs([KeyInput(VK_SHIFT, keyUp: false)]))
            {
                return false;
            }

            var remaining = characterCount;
            while (remaining > 0)
            {
                var chunkSize = Math.Min(remaining, 200);
                var inputs = new INPUT[chunkSize * 2];
                for (var i = 0; i < chunkSize; i++)
                {
                    inputs[i * 2] = KeyInput(VK_LEFT, keyUp: false);
                    inputs[i * 2 + 1] = KeyInput(VK_LEFT, keyUp: true);
                }

                if (!SendInputs(inputs))
                {
                    return false;
                }

                remaining -= chunkSize;
                if (remaining > 0)
                {
                    await Task.Delay(12, cancellationToken);
                }
            }

            return true;
        }
        finally
        {
            SendInputs([KeyInput(VK_SHIFT, keyUp: true)]);
        }
    }

    private static bool SendCtrlKey(ushort virtualKey)
    {
        var inputs = new INPUT[]
        {
            KeyInput(VK_CONTROL, keyUp: false),
            KeyInput(virtualKey, keyUp: false),
            KeyInput(virtualKey, keyUp: true),
            KeyInput(VK_CONTROL, keyUp: true)
        };

        return SendInputs(inputs);
    }

    private static bool SendInputs(INPUT[] inputs)
    {
        return SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT KeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUT_UNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0
                }
            }
        };
    }
}