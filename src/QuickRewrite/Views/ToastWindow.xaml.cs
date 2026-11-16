using System.Windows;
using System.Windows.Threading;

namespace QuickRewrite.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer;
    private Action? _action;

    public ToastWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    public void ShowMessage(string message, Action? action = null, int seconds = 8)
    {
        MessageText.Text = message;
        _action = action;
        UndoButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(2, seconds));

        Show();
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 18;
        Top = workArea.Bottom - ActualHeight - 18;
        _timer.Start();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        var action = _action;
        _action = null;
        Close();
        action?.Invoke();
    }
}