using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using QuickRewrite.Interop;
using QuickRewrite.Models;

namespace QuickRewrite.Services;

public sealed class SelectionService
{
    public const string NoSelectionError = "没有读取到选中的文字，请先选中一段文本。";

    private const int UiAutomationTimeoutMs = 1200;
    private const int ClipboardPollTimeoutMs = 700;
    private const int ClipboardAttempts = 2;

    private sealed record UiAutomationRead(string? Text, bool PasswordDetected, SelectionBounds? Bounds);

    public async Task<SelectionCaptureResult> CaptureAsync()
    {
        var targetWindow = NativeMethods.GetForegroundWindow();
        if (targetWindow == IntPtr.Zero)
        {
            return SelectionCaptureResult.Fail("未找到当前活动窗口。");
        }

        if (NativeMethods.GetWindowThreadProcessId(targetWindow, out var processId) == 0)
        {
            return SelectionCaptureResult.Fail("无法识别当前窗口。");
        }

        var currentProcessId = (uint)Environment.ProcessId;
        if (processId == currentProcessId)
        {
            return SelectionCaptureResult.Fail("请先回到要改写的应用，再使用快捷键。");
        }

        var windowTitle = GetWindowTitle(targetWindow);
        var processName = GetProcessName(processId);

        var uiAutomation = await ReadWithUiAutomationAsync();
        if (uiAutomation.PasswordDetected)
        {
            return SelectionCaptureResult.Fail("当前输入框不支持文本改写。");
        }

        if (!string.IsNullOrWhiteSpace(uiAutomation.Text))
        {
            return SelectionCaptureResult.Ok(new SelectionSnapshot(
                uiAutomation.Text.Trim(),
                targetWindow,
                windowTitle,
                processName,
                uiAutomation.Bounds));
        }

        var clipboardText = await RunOnStaThreadAsync(() => TryReadWithClipboard(targetWindow));
        if (!string.IsNullOrWhiteSpace(clipboardText))
        {
            var bounds = uiAutomation.Bounds ?? TryGetCaretBounds(targetWindow);
            return SelectionCaptureResult.Ok(new SelectionSnapshot(
                clipboardText.Trim(),
                targetWindow,
                windowTitle,
                processName,
                bounds));
        }

        return SelectionCaptureResult.Fail(NoSelectionError);
    }

    private static async Task<UiAutomationRead> ReadWithUiAutomationAsync()
    {
        try
        {
            return await RunOnStaThreadAsync(ReadWithUiAutomation)
                .WaitAsync(TimeSpan.FromMilliseconds(UiAutomationTimeoutMs));
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("UI Automation 读取选区超时，改用剪贴板读取。");
            return new UiAutomationRead(null, false, null);
        }
        catch
        {
            return new UiAutomationRead(null, false, null);
        }
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "QuickRewrite.SelectionProbe"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static UiAutomationRead ReadWithUiAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return new UiAutomationRead(null, false, null);
            }

            var isPasswordValue = focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
            if (isPasswordValue is bool isPassword && isPassword)
            {
                return new UiAutomationRead(null, true, null);
            }

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern textPattern)
            {
                return new UiAutomationRead(null, false, null);
            }

            var ranges = textPattern.GetSelection();
            if (ranges.Length == 0)
            {
                return new UiAutomationRead(null, false, null);
            }

            var bounds = GetSelectionBounds(ranges);
            var selectedText = string.Join(Environment.NewLine, ranges.Select(range => range.GetText(-1)));
            return new UiAutomationRead(
                string.IsNullOrWhiteSpace(selectedText) ? null : selectedText,
                false,
                bounds);
        }
        catch
        {
            return new UiAutomationRead(null, false, null);
        }
    }

    private static SelectionBounds? GetSelectionBounds(TextPatternRange[] ranges)
    {
        double left = double.MaxValue;
        double top = double.MaxValue;
        double right = double.MinValue;
        double bottom = double.MinValue;
        var found = false;

        foreach (var range in ranges)
        {
            Rect[] rectangles;
            try
            {
                rectangles = range.GetBoundingRectangles();
            }
            catch
            {
                continue;
            }

            foreach (var rectangle in rectangles)
            {
                if (rectangle.Width <= 0 || rectangle.Height <= 0)
                {
                    continue;
                }

                left = Math.Min(left, rectangle.Left);
                top = Math.Min(top, rectangle.Top);
                right = Math.Max(right, rectangle.Right);
                bottom = Math.Max(bottom, rectangle.Bottom);
                found = true;
            }
        }

        return found
            ? new SelectionBounds(left, top, right - left, bottom - top)
            : null;
    }

    private static string? TryReadWithClipboard(IntPtr targetWindow)
    {
        var snapshot = ClipboardSnapshot.Capture();
        try
        {
            for (var attempt = 0; attempt < ClipboardAttempts; attempt++)
            {
                EnsureTargetIsForeground(targetWindow);

                var previousSequence = NativeMethods.GetClipboardSequenceNumber();
                if (!KeyboardAutomation.SendCopy())
                {
                    return null;
                }

                var deadline = Environment.TickCount64 + ClipboardPollTimeoutMs;
                while (Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(25);
                    if (NativeMethods.GetClipboardSequenceNumber() == previousSequence)
                    {
                        continue;
                    }

                    try
                    {
                        if (System.Windows.Clipboard.ContainsText())
                        {
                            var text = System.Windows.Clipboard.GetText();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }
                    }
                    catch
                    {
                        // 剪贴板可能正被源应用占用，继续等待下一次读取。
                    }
                }

                Thread.Sleep(80);
            }

            return null;
        }
        finally
        {
            snapshot.Restore();
        }
    }

    private static void EnsureTargetIsForeground(IntPtr targetWindow)
    {
        try
        {
            if (NativeMethods.GetForegroundWindow() == targetWindow)
            {
                return;
            }

            if (!NativeMethods.IsWindow(targetWindow))
            {
                return;
            }

            NativeMethods.SetForegroundWindow(targetWindow);
            Thread.Sleep(80);
        }
        catch
        {
            // 焦点恢复属于尽力而为，失败时继续尝试复制。
        }
    }

    private static SelectionBounds? TryGetCaretBounds(IntPtr targetWindow)
    {
        try
        {
            var threadId = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
            if (threadId == 0)
            {
                return null;
            }

            var info = new NativeMethods.GUITHREADINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>()
            };

            if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == IntPtr.Zero)
            {
                return null;
            }

            var topLeft = new NativeMethods.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
            var bottomRight = new NativeMethods.POINT { X = info.rcCaret.Right, Y = info.rcCaret.Bottom };
            if (!NativeMethods.ClientToScreen(info.hwndCaret, ref topLeft) ||
                !NativeMethods.ClientToScreen(info.hwndCaret, ref bottomRight))
            {
                return null;
            }

            var width = Math.Max(bottomRight.X - topLeft.X, 1);
            var height = Math.Max(bottomRight.Y - topLeft.Y, 1);
            return new SelectionBounds(topLeft.X, topLeft.Y, width, height);
        }
        catch
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr window)
    {
        try
        {
            var length = NativeMethods.GetWindowTextLength(window);
            var builder = new StringBuilder(Math.Max(length + 1, 256));
            NativeMethods.GetWindowText(window, builder, builder.Capacity);
            return builder.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }
}