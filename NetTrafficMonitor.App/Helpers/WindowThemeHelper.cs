using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace NetTrafficMonitor.Helpers;

public static class WindowThemeHelper
{
    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0;
            }
        }
        catch { }
        return false;
    }

    public static void ApplyTitleBarTheme(Window window, bool isDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            RoutedEventHandler? handler = null;
            handler = (s, e) =>
            {
                window.Loaded -= handler;
                var h = new WindowInteropHelper(window).Handle;
                SetDarkMode(h, isDark);
            };
            window.Loaded += handler;
        }
        else
        {
            SetDarkMode(handle, isDark);
        }
    }

    private static void SetDarkMode(IntPtr hWnd, bool isDark)
    {
        if (hWnd == IntPtr.Zero) return;
        int useDarkMode = isDark ? 1 : 0;
        if (DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
        }
    }
}
