using System.Threading;
using System.Windows;
using System.Windows.Threading;
using QuickRewrite.Services;

namespace QuickRewrite;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\QuickRewrite.SingleInstance";
    private const string ActivationEventName = @"Local\QuickRewrite.Activate";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _activationCts;
    private Thread? _activationThread;
    private QuickRewriteController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            if (!SignalExistingInstance())
            {
                MessageBox.Show("QuickRewrite 已在运行。", "快捷改写", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        _ownsMutex = true;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ThemeService.ApplySystemTheme();

        try
        {
            ThemeService.StartMonitoring();
            _controller = new QuickRewriteController();
            _controller.Start();
            StartActivationListener();
        }
        catch (Exception ex)
        {
            AppLog.Error("应用启动失败", ex);
            MessageBox.Show($"QuickRewrite 启动失败：{ex.Message}", "快捷改写", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.StopMonitoring();
        _activationCts?.Cancel();
        _activationEvent?.Set();

        var activationThreadStopped = true;
        try
        {
            activationThreadStopped = _activationThread is null || _activationThread.Join(500);
        }
        catch
        {
            // 退出阶段的线程等待失败不影响关闭流程。
            activationThreadStopped = false;
        }

        _controller?.Dispose();
        if (activationThreadStopped)
        {
            _activationEvent?.Dispose();
            _activationCts?.Dispose();
        }

        if (_ownsMutex)
        {
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
                // 互斥量所有权可能已随线程结束丢失。
            }
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void StartActivationListener()
    {
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationCts = new CancellationTokenSource();

        _activationThread = new Thread(() =>
        {
            while (!_activationCts.IsCancellationRequested)
            {
                if (!_activationEvent.WaitOne(500))
                {
                    continue;
                }

                if (_activationCts.IsCancellationRequested)
                {
                    break;
                }

                Dispatcher.BeginInvoke(new Action(() => _controller?.ShowSettings()));
            }
        })
        {
            IsBackground = true,
            Name = "QuickRewrite.Activation"
        };

        _activationThread.Start();
    }

    private static bool SignalExistingInstance()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
                activationEvent.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("UI 线程未处理异常", e.Exception);
        e.Handled = true;
        MessageBox.Show($"发生未处理错误：{e.Exception.Message}", "快捷改写", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("应用域未处理异常", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        AppLog.Error("未观察的任务异常", e.Exception);
    }
}
