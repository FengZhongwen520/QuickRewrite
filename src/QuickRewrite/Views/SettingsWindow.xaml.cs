using System.Windows;
using System.Windows.Input;
using QuickRewrite.Models;
using QuickRewrite.Services;

namespace QuickRewrite.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _draft;

    public event Action<AppSettings>? SettingsSaved;

    public SettingsWindow(AppSettings currentSettings, SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _draft = currentSettings.Clone();

        HotkeyBox.Text = _draft.Hotkey;
        DemoProviderOption.IsChecked = _draft.Provider == AiProviderKind.Demo;
        ApiProviderOption.IsChecked = _draft.Provider != AiProviderKind.Demo;
        BaseUrlBox.Text = _draft.BaseUrl;
        ModelBox.Text = _draft.Model;
        ApiKeyBox.Password = _draft.ApiKey;
        TimeoutBox.Text = _draft.TimeoutSeconds.ToString();
        MaxInputBox.Text = _draft.MaxInputCharacters.ToString();
        SettingsPathText.Text = $"配置文件：{settingsService.SettingsPath}";
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = $"版本 {version?.ToString(3) ?? "1.0.0"}";
        UpdateApiFields();
        SourceInitialized += (_, _) => ThemeService.ApplyWindowChrome(this);
    }

    private void Provider_Checked(object sender, RoutedEventArgs e)
    {
        UpdateApiFields();
    }

    private void UpdateApiFields()
    {
        if (ApiFields is null)
        {
            return;
        }

        var isApiProvider = ApiProviderOption.IsChecked == true;
        ApiFields.IsEnabled = isApiProvider;
        ApiFields.Opacity = isApiProvider ? 1 : 0.5;
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "Ctrl+Alt+R";
        HotkeyBox.Focus();
        HotkeyBox.CaretIndex = HotkeyBox.Text.Length;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        if (!HotkeyGesture.TryParse(HotkeyBox.Text, out var gesture, out var hotkeyError) || gesture is null)
        {
            ErrorText.Text = hotkeyError;
            return;
        }

        if (!int.TryParse(TimeoutBox.Text.Trim(), out var timeout) || timeout is < 5 or > 120)
        {
            ErrorText.Text = "超时时间请输入 5～120 之间的整数。";
            return;
        }

        if (!int.TryParse(MaxInputBox.Text.Trim(), out var maxInput) || maxInput is < 100 or > 50000)
        {
            ErrorText.Text = "最大输入字数请输入 100～50,000 之间的整数。";
            return;
        }

        var isApiProvider = ApiProviderOption.IsChecked == true;
        if (isApiProvider)
        {
            if (!Uri.TryCreate(BaseUrlBox.Text.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ErrorText.Text = "Base URL 需要是有效的 http 或 https 地址。";
                return;
            }

            if (string.IsNullOrWhiteSpace(ModelBox.Text))
            {
                ErrorText.Text = "请填写模型名称。";
                return;
            }
        }

        _draft.Hotkey = gesture.DisplayName;
        _draft.Provider = isApiProvider ? AiProviderKind.OpenAiCompatible : AiProviderKind.Demo;
        _draft.BaseUrl = BaseUrlBox.Text.Trim();
        _draft.Model = ModelBox.Text.Trim();
        _draft.ApiKey = ApiKeyBox.Password;
        _draft.TimeoutSeconds = timeout;
        _draft.MaxInputCharacters = maxInput;

        try
        {
            _settingsService.Save(_draft);
            SettingsSaved?.Invoke(_draft.Clone());
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"保存失败：{ex.Message}";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
