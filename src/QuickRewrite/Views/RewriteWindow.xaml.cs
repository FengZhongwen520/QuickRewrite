using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickRewrite.Interop;
using QuickRewrite.Models;
using QuickRewrite.Services;

namespace QuickRewrite.Views;

public partial class RewriteWindow : Window
{
    private readonly SelectionSnapshot _selection;
    private readonly string _originalText;
    private readonly AiRewriteService _aiService;
    private readonly Func<string, Task<bool>> _replaceAsync;
    private readonly DispatcherTimer _loadingTimer;

    private RewriteModeDefinition? _selectedMode;
    private string? _customInstruction;
    private string _resultText = "";
    private CancellationTokenSource? _generationCts;
    private bool _busy;
    private bool _suppressDeactivatedClose;
    private bool _closing;
    private bool _closed;
    private long _ignoreDeactivationUntil;
    private int _generationVersion;
    private int _loadingStep;
    private TranslateTransform? _rainbowShift;
    private RotateTransform? _rainbowSpin;
    private TranslateTransform? _rainbowGlowShift;
    private RotateTransform? _rainbowGlowSpin;

    public RewriteWindow(
        SelectionSnapshot selection,
        AiRewriteService aiService,
        Func<string, Task<bool>> replaceAsync)
    {
        InitializeComponent();
        _selection = selection;
        _originalText = selection.Text;
        _aiService = aiService;
        _replaceAsync = replaceAsync;

        ModeList.ItemsSource = RewriteModes.All;
        OriginalText.Text = _originalText;
        OriginalCount.Text = $"{_originalText.Length:N0} 字";

        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _loadingTimer.Tick += (_, _) => AdvanceLoadingStatus();

        SourceInitialized += (_, _) => ThemeService.ApplyWindowChrome(this);
        Deactivated += (_, _) =>
        {
            if (_closing || _closed || _suppressDeactivatedClose || Environment.TickCount64 < _ignoreDeactivationUntil)
            {
                return;
            }

            CloseSafely();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _generationVersion++;
            _generationCts?.Cancel();
            _loadingTimer.Stop();
            StopLoadingAnimations();
        };
    }

    public void ShowNearSelection(SelectionSnapshot selection)
    {
        var point = new NativeMethods.POINT();
        NativeMethods.GetCursorPos(out point);

        var anchorLeft = (double)point.X;
        var anchorTop = (double)point.Y;
        var anchorRight = (double)point.X;
        var anchorBottom = (double)point.Y;
        if (selection.Bounds is { Width: > 0, Height: > 0 } bounds)
        {
            anchorLeft = bounds.Left;
            anchorTop = bounds.Top;
            anchorRight = bounds.Left + bounds.Width;
            anchorBottom = bounds.Top + bounds.Height;
        }

        var dpi = NativeMethods.GetDpiForWindow(selection.TargetWindow);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        Left = ((anchorLeft + anchorRight) / 2 / scale) - (Width / 2);
        Top = (anchorTop / scale) - Height - 8;

        _ignoreDeactivationUntil = Environment.TickCount64 + 600;
        Opacity = 0;
        Show();
        UpdateLayout();

        PlaceAboveSelection(anchorLeft, anchorTop, anchorRight, anchorBottom);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            PlaceAboveSelection(anchorLeft, anchorTop, anchorRight, anchorBottom)));

        Activate();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private void PlaceAboveSelection(double anchorLeft, double anchorTop, double anchorRight, double anchorBottom)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var windowRect))
        {
            return;
        }

        var windowWidth = Math.Max(windowRect.Right - windowRect.Left, 1);
        var windowHeight = Math.Max(windowRect.Bottom - windowRect.Top, 1);

        var anchorCenter = new NativeMethods.POINT
        {
            X = (int)Math.Round((anchorLeft + anchorRight) / 2),
            Y = (int)Math.Round((anchorTop + anchorBottom) / 2)
        };

        var monitor = NativeMethods.MonitorFromPoint(anchorCenter, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        var left = ((anchorLeft + anchorRight) / 2) - (windowWidth / 2.0);
        var top = anchorTop - windowHeight - 8;
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            if (top < info.rcWork.Top + 8)
            {
                top = anchorBottom + 8;
            }

            var minLeft = info.rcWork.Left + 8.0;
            var maxLeft = Math.Max(minLeft, info.rcWork.Right - windowWidth - 8.0);
            var minTop = info.rcWork.Top + 8.0;
            var maxTop = Math.Max(minTop, info.rcWork.Bottom - windowHeight - 8.0);
            left = Math.Clamp(left, minLeft, maxLeft);
            top = Math.Clamp(top, minTop, maxTop);
        }

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            (int)Math.Round(left),
            (int)Math.Round(top),
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    private async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not RadioButton { Tag: RewriteModeDefinition mode })
        {
            return;
        }

        if (mode.IsCustom)
        {
            _selectedMode = mode;
            _customInstruction = null;
            CustomPanel.Visibility = Visibility.Visible;
            InstructionBox.Focus();
            StatusText.Text = "输入自定义要求后开始改写。";
            return;
        }

        CustomPanel.Visibility = Visibility.Collapsed;
        await StartRewriteAsync(mode, null);
    }

    private void CustomSubmit_Click(object sender, RoutedEventArgs e)
    {
        SubmitCustomInstruction();
    }

    private void InstructionBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SubmitCustomInstruction();
        }
    }

    private async void SubmitCustomInstruction()
    {
        if (_busy)
        {
            return;
        }

        var instruction = InstructionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(instruction))
        {
            StatusText.Text = "请输入你想要的修改要求。";
            InstructionBox.Focus();
            return;
        }

        if (instruction.Length > 200)
        {
            StatusText.Text = "修改要求最多 200 个字符。";
            InstructionBox.Focus();
            return;
        }

        var customMode = RewriteModes.All.First(mode => mode.Kind == RewriteModeKind.Custom);
        await StartRewriteAsync(customMode, instruction);
    }

    private async Task StartRewriteAsync(RewriteModeDefinition mode, string? instruction)
    {
        _selectedMode = mode;
        _customInstruction = instruction;
        _resultText = "";
        ModeLabel.Text = mode.Title;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultText.Visibility = Visibility.Collapsed;
        ResultText.Text = "";
        ResultCount.Text = "";
        await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        if (_selectedMode is null || _busy)
        {
            return;
        }

        _generationCts?.Cancel();
        _generationCts = new CancellationTokenSource();
        var cts = _generationCts;
        var version = ++_generationVersion;

        _busy = true;
        ModeList.IsEnabled = false;
        CustomPanel.IsEnabled = false;
        ErrorPanel.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultText.Visibility = Visibility.Collapsed;
        ResultText.Text = "";
        ResultCount.Text = "";
        RegenerateButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        StatusText.Text = "正在生成，请稍候…";
        SetLoading(true);

        try
        {
            var result = await _aiService.RewriteAsync(
                _originalText,
                _selectedMode,
                _customInstruction,
                cts.Token);

            if (version != _generationVersion)
            {
                return;
            }

            _resultText = result;
            SetLoading(false);
            ErrorPanel.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ResultText.Text = result;
            ResultText.Visibility = Visibility.Visible;
            ResultCount.Text = $"{result.Length:N0} 字";
            RegenerateButton.IsEnabled = false;
            CopyButton.IsEnabled = true;
            ReplaceButton.Visibility = Visibility.Visible;
            ReplaceButton.IsEnabled = true;
            StatusText.Text = string.Equals(result, _originalText, StringComparison.Ordinal)
                ? "结果与原文一致，可以换一种改写方式。"
                : "改写完成，点击“确认改写”替换原文。";

            ResultText.Opacity = 0;
            ResultText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
        catch (OperationCanceledException)
        {
            if (version == _generationVersion)
            {
                SetLoading(false);
                ShowError("改写已取消。");
            }
        }
        catch (Exception ex)
        {
            if (version == _generationVersion)
            {
                SetLoading(false);
                ShowError(ex.Message);
            }
        }
        finally
        {
            if (version == _generationVersion)
            {
                _busy = false;
                ModeList.IsEnabled = true;
                CustomPanel.IsEnabled = true;
                RegenerateButton.IsEnabled = true;
            }
        }
    }

    private void SetLoading(bool loading)
    {
        LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        if (!loading)
        {
            _loadingTimer.Stop();
            StopLoadingAnimations();
            return;
        }

        _loadingStep = 0;
        LoadingStatus.Text = "理解原文…";
        _loadingTimer.Start();
        StartLoadingAnimations();
    }

    private void ShowError(string message)
    {
        ErrorPanel.Visibility = Visibility.Visible;
        ErrorText.Text = message;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        ResultText.Visibility = Visibility.Collapsed;
        CopyButton.IsEnabled = false;
        ReplaceButton.IsEnabled = false;
        RegenerateButton.IsEnabled = true;
        StatusText.Text = "生成失败，可直接重试或切换改写方式。";
    }

    private void AdvanceLoadingStatus()
    {
        string[] steps =
        [
            "理解原文…",
            "调整表达…",
            "检查细节…",
            "即将完成…"
        ];

        _loadingStep = (_loadingStep + 1) % steps.Length;
        LoadingStatus.Text = steps[_loadingStep];
    }

    private void StartLoadingAnimations()
    {
        var (shift, spin) = EnsureRainbowTransform(LoadingPill, RainbowShift, RainbowSpin);
        _rainbowShift = shift;
        _rainbowSpin = spin;
        shift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(3000))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
        spin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(6000))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });

        var (glowShift, glowSpin) = EnsureRainbowTransform(LoadingGlow, RainbowGlowShift, RainbowGlowSpin);
        _rainbowGlowShift = glowShift;
        _rainbowGlowSpin = glowSpin;
        glowShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 0.5, TimeSpan.FromMilliseconds(4200))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
        glowSpin.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, -360, TimeSpan.FromMilliseconds(9000))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
    }

    private void StopLoadingAnimations()
    {
        _rainbowShift?.BeginAnimation(TranslateTransform.XProperty, null);
        if (_rainbowShift is not null)
        {
            _rainbowShift.X = 0;
        }

        _rainbowSpin?.BeginAnimation(RotateTransform.AngleProperty, null);
        if (_rainbowSpin is not null)
        {
            _rainbowSpin.Angle = 0;
        }

        _rainbowGlowShift?.BeginAnimation(TranslateTransform.XProperty, null);
        if (_rainbowGlowShift is not null)
        {
            _rainbowGlowShift.X = 0;
        }

        _rainbowGlowSpin?.BeginAnimation(RotateTransform.AngleProperty, null);
        if (_rainbowGlowSpin is not null)
        {
            _rainbowGlowSpin.Angle = 0;
        }
    }

    private static (TranslateTransform Shift, RotateTransform Spin) EnsureRainbowTransform(
        Border border,
        TranslateTransform shiftFallback,
        RotateTransform spinFallback)
    {
        if (border.BorderBrush is not LinearGradientBrush brush)
        {
            return (shiftFallback, spinFallback);
        }

        if (brush.IsFrozen)
        {
            brush = brush.Clone();
            border.BorderBrush = brush;
        }

        if (brush.RelativeTransform is TransformGroup group
            && group.Children.Count >= 2
            && group.Children[0] is TranslateTransform shift
            && group.Children[1] is RotateTransform spin)
        {
            return (shift, spin);
        }

        return (shiftFallback, spinFallback);
    }

    private async void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedMode is null)
        {
            return;
        }

        if (_selectedMode.IsCustom)
        {
            SubmitCustomInstruction();
            return;
        }

        await GenerateAsync();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_resultText))
        {
            return;
        }

        try
        {
            if (ClipboardSnapshot.TrySetText(_resultText))
            {
                StatusText.Text = "已复制到剪贴板。";
            }
            else
            {
                StatusText.Text = "复制失败：剪贴板正被其他程序占用。";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"复制失败：{ex.Message}";
        }
    }

    private async void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_resultText) || _busy)
        {
            return;
        }

        _busy = true;
        ReplaceButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        ModeList.IsEnabled = false;
        StatusText.Text = "正在写回原窗口…";

        var success = await TryReplaceResultAsync();
        if (_closing || _closed)
        {
            return;
        }

        if (success)
        {
            CloseSafely();
            return;
        }
        else
        {
            StatusText.Text = "没有成功写回原窗口，请使用“复制结果”手动粘贴。";
            ReplaceButton.IsEnabled = true;
            CopyButton.IsEnabled = true;
            ModeList.IsEnabled = true;
            RegenerateButton.IsEnabled = true;
        }

        _busy = false;
    }

    private async Task<bool> TryReplaceResultAsync()
    {
        _suppressDeactivatedClose = true;
        _ignoreDeactivationUntil = Environment.TickCount64 + 1200;

        var success = false;
        try
        {
            success = await _replaceAsync(_resultText);
        }
        catch
        {
            success = false;
        }
        finally
        {
            if (!success && !_closing && !_closed && !IsVisible)
            {
                try
                {
                    Show();
                    Activate();
                }
                catch (InvalidOperationException)
                {
                    // 窗口已在替换过程中被关闭，无需再恢复显示。
                }
            }

            _suppressDeactivatedClose = false;
            _ignoreDeactivationUntil = Environment.TickCount64 + 450;
        }

        return success;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    public void CloseSafely()
    {
        if (_closing || _closed)
        {
            return;
        }

        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // 关闭过程中再次收到关闭请求时忽略。
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseSafely();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSafely();
            return;
        }

        if (e.Key == Key.F5 && !_busy && _resultText.Length > 0)
        {
            e.Handled = true;
            if (_selectedMode?.IsCustom == true)
            {
                SubmitCustomInstruction();
            }
            else
            {
                _ = GenerateAsync();
            }
        }
    }

}
