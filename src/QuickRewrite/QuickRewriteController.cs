using System.Windows;
using QuickRewrite.Interop;
using QuickRewrite.Models;
using QuickRewrite.Services;
using QuickRewrite.Views;

namespace QuickRewrite;

public sealed class QuickRewriteController : IDisposable
{
    private readonly SettingsService _settingsService = new();
    private readonly SelectionService _selectionService = new();
    private readonly TextInjector _textInjector = new();
    private readonly HotkeyService _hotkeyService = new();
    private readonly TrayService _trayService = new();

    private AppSettings _settings;
    private AiRewriteService _aiService;
    private RewriteWindow? _activePopup;
    private SettingsWindow? _settingsWindow;
    private ToastWindow? _toastWindow;
    private ReplacementUndo? _lastReplacement;
    private bool _replacing;
    private bool _disposed;

    private sealed record ReplacementUndo(
        SelectionSnapshot Selection,
        string OriginalText,
        string RewrittenText,
        long ExpiresAt);

    public QuickRewriteController()
    {
        _settings = _settingsService.Load();
        _aiService = new AiRewriteService(_settings);

        _hotkeyService.Pressed += OnHotkeyPressed;
        _trayService.SettingsRequested += OpenSettings;
        _trayService.ExitRequested += ExitApplication;
    }

    public void Start()
    {
        var configuredHotkey = _settings.Hotkey;
        if (!HotkeyGesture.TryParse(_settings.Hotkey, out var gesture, out var parseError) || gesture is null)
        {
            _trayService.ShowWarning("快捷键配置有误", parseError);
            gesture = HotkeyGesture.TryParse("Ctrl+Alt+R", out var fallback, out _) ? fallback! : new HotkeyGesture(0, 0, "");
            _settings.Hotkey = gesture.DisplayName;
            try
            {
                _settingsService.Save(_settings);
            }
            catch
            {
                // 配置回写失败不影响本次启动。
            }
        }

        if (!_hotkeyService.Register(gesture, out var registerError))
        {
            var fallback = HotkeyGesture.TryParse("Ctrl+Alt+R", out var defaultGesture, out _) ? defaultGesture : null;
            if (fallback is not null &&
                !string.Equals(fallback.DisplayName, configuredHotkey, StringComparison.OrdinalIgnoreCase) &&
                _hotkeyService.Register(fallback, out _))
            {
                _settings.Hotkey = fallback.DisplayName;
                try
                {
                    _settingsService.Save(_settings);
                }
                catch
                {
                    // 配置回写失败不影响本次启动。
                }

                _trayService.ShowWarning("快捷键不可用", $"{registerError} 已临时改用 {fallback.DisplayName}。");
            }
            else
            {
                _trayService.ShowWarning("快捷键不可用", registerError);
            }
        }
        else
        {
            _trayService.ShowInfo("QuickRewrite 已启动", $"选中文字后按 {gesture.DisplayName} 唤起快捷改写。");
        }
    }

    private async void OnHotkeyPressed()
    {
        try
        {
            if (_replacing)
            {
                ShowToast("正在写回原文，请稍候。", null, 3);
                return;
            }

            if (_activePopup is not null)
            {
                if (_activePopup.IsVisible)
                {
                    _activePopup.Activate();
                    return;
                }

                _activePopup.CloseSafely();
                _activePopup = null;
            }

            if (!await WaitForHotkeyModifiersReleasedAsync())
            {
                ShowToast("快捷键仍处于按下状态，请松开按键后再试。");
                return;
            }

            // Let the target app process the final key-up messages before sending Ctrl+C.
            await Task.Delay(120);

            SelectionCaptureResult capture;
            for (var attempt = 0; ; attempt++)
            {
                capture = await _selectionService.CaptureAsync();
                if (capture.Success ||
                    attempt >= 1 ||
                    capture.Error != SelectionService.NoSelectionError)
                {
                    break;
                }

                await Task.Delay(150 + (attempt * 100));
            }

            if (!capture.Success || capture.Snapshot is null)
            {
                ShowToast(capture.Error ?? "未读取到选中文字。");
                return;
            }

            var selection = capture.Snapshot;
            if (selection.Text.Length > _settings.MaxInputCharacters)
            {
                ShowToast($"选中的文字超过 {_settings.MaxInputCharacters:N0} 字，请缩短后再试。");
                return;
            }

            var rewriteWindow = new RewriteWindow(
                selection,
                _aiService,
                text => ReplaceAsync(selection, text));

            rewriteWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activePopup, rewriteWindow))
                {
                    _activePopup = null;
                }
            };

            _activePopup = rewriteWindow;
            rewriteWindow.ShowNearSelection(selection);
        }
        catch (Exception ex)
        {
            ShowToast($"唤起改写菜单失败：{ex.Message}");
        }
    }

    private async Task<bool> ReplaceAsync(SelectionSnapshot selection, string resultText)
    {
        if (_replacing)
        {
            return false;
        }

        _replacing = true;
        try
        {
            if (_activePopup is RewriteWindow resultWindow)
            {
                resultWindow.Hide();
            }

            await Task.Delay(90);
            var success = await _textInjector.PasteAsync(resultText, selection.TargetWindow);
            if (success)
            {
                _lastReplacement = new ReplacementUndo(
                    selection,
                    selection.Text,
                    resultText,
                    Environment.TickCount64 + 10_000);

                ShowToast("已替换原文，10 秒内可撤销。", () => _ = UndoLastReplacementAsync(), 10);
            }

            return success;
        }
        finally
        {
            _replacing = false;
        }
    }

    private async Task UndoLastReplacementAsync()
    {
        var undo = _lastReplacement;
        _lastReplacement = null;

        if (undo is null)
        {
            ShowToast("没有可撤销的改写。", null, 4);
            return;
        }

        if (Environment.TickCount64 > undo.ExpiresAt)
        {
            var copied = ClipboardSnapshot.TrySetText(undo.OriginalText);
            ShowToast(
                copied ? "撤销时间已过，原文已复制到剪贴板。" : "撤销时间已过，请手动恢复原文。",
                null,
                6);
            return;
        }

        var success = await _textInjector.RestoreAsync(
            undo.OriginalText,
            undo.RewrittenText,
            undo.Selection.TargetWindow);

        if (success)
        {
            ShowToast("已恢复改写前的原文。", null, 4);
            return;
        }

        var originalCopied = ClipboardSnapshot.TrySetText(undo.OriginalText);
        ShowToast(
            originalCopied
                ? "撤销失败，原文已复制到剪贴板，可手动粘贴。"
                : "撤销失败，请手动恢复原文。",
            null,
            8);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _settingsService);
        _settingsWindow.SettingsSaved += ApplySettings;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplySettings(AppSettings settings)
    {
        var previousHotkey = _settings.Hotkey;
        _settings = settings.Clone();
        _aiService = new AiRewriteService(_settings);

        if (HotkeyGesture.TryParse(_settings.Hotkey, out var gesture, out _) && gesture is not null)
        {
            if (!_hotkeyService.Register(gesture, out var error))
            {
                _settings.Hotkey = previousHotkey;
                try
                {
                    _settingsService.Save(_settings);
                }
                catch
                {
                    // 回滚配置失败时仍继续恢复旧快捷键。
                }

                if (HotkeyGesture.TryParse(previousHotkey, out var previousGesture, out _) &&
                    previousGesture is not null)
                {
                    _hotkeyService.Register(previousGesture, out _);
                }

                _trayService.ShowWarning("快捷键注册失败，已恢复原快捷键", error);
            }
            else
            {
                _trayService.ShowInfo("设置已保存", $"快捷改写快捷键：{gesture.DisplayName}");
            }
        }
        else
        {
            _settings.Hotkey = previousHotkey;
            try
            {
                _settingsService.Save(_settings);
            }
            catch
            {
                // 回滚配置失败时仍继续恢复旧快捷键。
            }

            if (HotkeyGesture.TryParse(previousHotkey, out var previousGesture, out _) &&
                previousGesture is not null)
            {
                _hotkeyService.Register(previousGesture, out _);
            }

            _trayService.ShowWarning("快捷键格式有误，已恢复原快捷键", "请使用 Ctrl+Alt+R 这类格式。");
        }
    }

    public void ShowSettings() => OpenSettings();

    private static async Task<bool> WaitForHotkeyModifiersReleasedAsync()
    {
        var deadline = Environment.TickCount64 + 1200;
        while (Environment.TickCount64 < deadline)
        {
            if (!IsAnyHotkeyModifierDown())
            {
                return true;
            }

            await Task.Delay(15);
        }

        return !IsAnyHotkeyModifierDown();
    }

    private static bool IsAnyHotkeyModifierDown()
    {
        return NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL)
            || NativeMethods.IsKeyDown(NativeMethods.VK_MENU)
            || NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)
            || NativeMethods.IsKeyDown(NativeMethods.VK_LWIN)
            || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);
    }

    private void ShowToast(string message, Action? action = null, int seconds = 8)
    {
        _toastWindow?.Close();
        var toast = new ToastWindow();
        _toastWindow = toast;
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_toastWindow, toast))
            {
                _toastWindow = null;
            }
        };
        toast.ShowMessage(message, action, seconds);
    }

    private void ExitApplication()
    {
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _hotkeyService.Dispose();
        _trayService.Dispose();
        _toastWindow?.Close();
        _settingsWindow?.Close();
        _activePopup?.CloseSafely();
    }
}
