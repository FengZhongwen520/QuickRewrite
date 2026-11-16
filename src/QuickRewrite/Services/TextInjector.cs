using QuickRewrite.Interop;

namespace QuickRewrite.Services;

public sealed class TextInjector
{
    public async Task<bool> PasteAsync(string text, IntPtr targetWindow, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text) || targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(targetWindow))
        {
            return false;
        }

        var clipboardSnapshot = ClipboardSnapshot.Capture();
        try
        {
            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                NativeMethods.SetForegroundWindow(targetWindow);
                await Task.Delay(140, cancellationToken);
            }

            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return false;
            }

            if (!ClipboardSnapshot.TrySetText(text))
            {
                return false;
            }

            await Task.Delay(50, cancellationToken);
            if (!KeyboardAutomation.SendPaste())
            {
                return false;
            }

            await Task.Delay(GetPasteSettleDelay(text), cancellationToken);
            if (!TextSelectionService.TrySelectPreviousText(text))
            {
                await KeyboardAutomation.SelectPreviousTextAsync(text, cancellationToken);
            }

            await Task.Delay(80, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            clipboardSnapshot.Restore();
        }
    }

    public async Task<bool> RestoreAsync(
        string originalText,
        string rewrittenText,
        IntPtr targetWindow,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(originalText) ||
            targetWindow == IntPtr.Zero ||
            !NativeMethods.IsWindow(targetWindow))
        {
            return false;
        }

        try
        {
            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                NativeMethods.SetForegroundWindow(targetWindow);
                await Task.Delay(140, cancellationToken);
            }

            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return false;
            }

            if (TextSelectionService.TrySelectPreviousText(rewrittenText))
            {
                return await PasteAsync(originalText, targetWindow, cancellationToken);
            }

            // 无法可靠定位改写结果时，退回目标应用自身的撤销操作。
            return await UndoAsync(targetWindow, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UndoAsync(IntPtr targetWindow, CancellationToken cancellationToken = default)
    {
        if (targetWindow == IntPtr.Zero || !NativeMethods.IsWindow(targetWindow))
        {
            return false;
        }

        try
        {
            NativeMethods.SetForegroundWindow(targetWindow);
            await Task.Delay(140, cancellationToken);
            return NativeMethods.GetForegroundWindow() == targetWindow && KeyboardAutomation.SendUndo();
        }
        catch
        {
            return false;
        }
    }

    private static int GetPasteSettleDelay(string text)
    {
        // 长文本粘贴更慢，给目标控件留出更新时间再重新选中结果。
        return Math.Clamp(180 + (text.Length / 20), 180, 700);
    }
}
