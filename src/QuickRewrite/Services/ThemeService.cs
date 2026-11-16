using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace QuickRewrite.Services;

public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int DwmWindowCornerRound = 2;
    private const int DwmBackdropMainWindow = 2;

    public static bool IsDark { get; private set; }

    public static void StartMonitoring()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void StopMonitoring()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
        {
            return;
        }

        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        try
        {
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplySystemTheme();
                foreach (Window window in app.Windows)
                {
                    if (window.AllowsTransparency)
                    {
                        continue;
                    }

                    ApplyWindowChrome(window);
                }
            }));
        }
        catch
        {
            // 应用正在退出时 Dispatcher 可能已停止接收任务。
        }
    }

    public static void ApplySystemTheme()
    {
        IsDark = ReadSystemTheme();
        var resources = System.Windows.Application.Current.Resources;

        if (IsDark)
        {
            SetColor(resources, "AppBackgroundBrush", "#FF202020");
            SetColor(resources, "PanelBrush", "#FF292929");
            SetColor(resources, "PanelRaisedBrush", "#FF2D2D2D");
            SetColor(resources, "CardFillBrush", "#FF2B2B2B");
            SetColor(resources, "CardHoverBrush", "#FF343434");
            SetColor(resources, "ControlFillBrush", "#12FFFFFF");
            SetColor(resources, "HoverFillBrush", "#18FFFFFF");
            SetColor(resources, "EditorSurfaceBrush", "#FF1C1C1C");
            SetColor(resources, "LoadingLayerBrush", "#F21F1F1F");
            SetColor(resources, "BorderSubtleBrush", "#20FFFFFF");
            SetColor(resources, "BorderStrongBrush", "#35FFFFFF");
            SetColor(resources, "TextPrimaryBrush", "#FFFFFFFF");
            SetColor(resources, "TextSecondaryBrush", "#FFC7C7C7");
            SetColor(resources, "TextMutedBrush", "#FF969696");
            SetColor(resources, "AccentBrush", "#FF60CDFF");
            SetColor(resources, "AccentSoftBrush", "#2260CDFF");
            SetColor(resources, "DangerBrush", "#FFFF99A4");
            resources["AccentColor"] = Color.FromRgb(0x60, 0xCD, 0xFF);
        }
        else
        {
            SetColor(resources, "AppBackgroundBrush", "#FFF3F3F3");
            SetColor(resources, "PanelBrush", "#FFFFFFFF");
            SetColor(resources, "PanelRaisedBrush", "#FFFFFFFF");
            SetColor(resources, "CardFillBrush", "#FFFFFFFF");
            SetColor(resources, "CardHoverBrush", "#FFF9F9F9");
            SetColor(resources, "ControlFillBrush", "#0A000000");
            SetColor(resources, "HoverFillBrush", "#10000000");
            SetColor(resources, "EditorSurfaceBrush", "#FFFFFFFF");
            SetColor(resources, "LoadingLayerBrush", "#F5F9F9F9");
            SetColor(resources, "BorderSubtleBrush", "#1F000000");
            SetColor(resources, "BorderStrongBrush", "#33000000");
            SetColor(resources, "TextPrimaryBrush", "#FF1A1A1A");
            SetColor(resources, "TextSecondaryBrush", "#FF5D5D5D");
            SetColor(resources, "TextMutedBrush", "#FF777777");
            SetColor(resources, "AccentBrush", "#FF0067C0");
            SetColor(resources, "AccentSoftBrush", "#180067C0");
            SetColor(resources, "DangerBrush", "#FFC42B1C");
            resources["AccentColor"] = Color.FromRgb(0x00, 0x67, 0xC0);
        }
    }

    public static void ApplyWindowChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var darkMode = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));

        var cornerPreference = DwmWindowCornerRound;
        DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref cornerPreference, sizeof(int));

        var backdrop = DwmBackdropMainWindow;
        DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));

        window.Background = (Brush)System.Windows.Application.Current.Resources["AppBackgroundBrush"];
    }

    private static bool ReadSystemTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);
            return value is int lightTheme && lightTheme == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetColor(ResourceDictionary resources, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
