using System;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;
using NetTrafficMonitor.Core.Data;
using NetTrafficMonitor.Core.Models;
using NetTrafficMonitor.Core.Services;
using NetTrafficMonitor.Service;
using NetTrafficMonitor.ViewModels;
using NetTrafficMonitor.Views;

namespace NetTrafficMonitor;

public partial class App : System.Windows.Application
{
    private NotifyIcon? _trayIcon;
    private NetworkMonitorService? _monitor;
    private UserPreferences? _prefs;
    private SqliteConnection? _conn;
    private MainWindow? _settingsWindow;
    private HudWindow? _hudWindow;

    private string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NetTrafficMonitor",
        "data.db");

    private const string MutexName = "NetTrafficMonitor-SingleInstance";

    public bool MinimizeToTray
    {
        get => _prefs?.MinimizeToTray ?? false;
        set { if (_prefs is not null) _prefs.MinimizeToTray = value; }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        // Init DB
        var dbInit = new DatabaseInitializer(DbPath);
        await dbInit.InitializeAsync();
        _conn = dbInit.CreateConnection();

        // Load preferences
        _prefs = new UserPreferences();
        await _prefs.LoadAsync(_conn);

        // Apply selected theme
        ApplyTheme();

        // Start monitor
        _monitor = new NetworkMonitorService(DbPath);
        await _monitor.InitializeAsync();
        _monitor.SpeedUpdated += OnSpeedUpdated;

        // Restore selected adapter
        if (_prefs.SelectedAdapterId > 0)
            await _monitor.SelectAdapterAsync(_prefs.SelectedAdapterId);

        // Auto-select if nothing selected yet
        if (_monitor.CurrentAdapterId <= 0)
        {
            var adapters = await _monitor.RefreshAdaptersAsync();
            if (adapters.Count > 0)
                await _monitor.SelectAdapterAsync(adapters[0].Id);
        }

        _monitor.Start();

        CreateTrayIcon();

        if (!_prefs.StartMinimized)
            ShowSettingsWindow();
    }

    public static void ApplyTheme(Theme? theme = null)
    {
        try
        {
            var app = (App?)System.Windows.Application.Current;
            if (app is null) return;

            var selected = theme ?? app._prefs?.Theme ?? Theme.System;

            app.Dispatcher.InvokeAsync(() =>
            {
                var resources = System.Windows.Application.Current?.Resources as ResourceDictionary;
                if (resources == null) return;

                var merged = resources.MergedDictionaries;
                bool hasLight = merged.Any(d =>
                    d.Source is not null &&
                    d.Source.OriginalString.EndsWith("LightStyles.xaml", StringComparison.OrdinalIgnoreCase));

                if (selected == Theme.Dark)
                {
                    // Remove Light override so only dark base/styles remain
                    if (hasLight)
                    {
                        var light = merged.First(d =>
                            d.Source is not null &&
                            d.Source.OriginalString.EndsWith("LightStyles.xaml", StringComparison.OrdinalIgnoreCase));
                        merged.Remove(light);
                    }
                }
                else // Light or System
                {
                    // Ensure LightStyles.xaml is present so brushes resolve to light palette
                    if (!hasLight)
                    {
                        merged.Add(new ResourceDictionary
                        {
                            Source = new Uri("Resources/LightStyles.xaml", UriKind.Relative)
                        });
                    }
                }
                
                app.UpdateTrayMenuTheme(selected == Theme.Dark);
            });
        }
        catch
        {
            // ignore theme switching errors (safe fallback: keep current resources)
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = GetOrCreateTrayIcon(),
            Text = "NetTrafficMonitor\n↓ 0 B/s\n↑ 0 B/s",
            Visible = true
        };

        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false
        };
        menu.Items.Add("Show Settings", null, (_, _) => ShowSettingsWindow());
        menu.Items.Add("Toggle HUD", null, (_, _) => ToggleHud());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ShutdownAsync());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.MouseDoubleClick += (_, _) => ShowSettingsWindow();
        
        UpdateTrayMenuTheme((_prefs?.Theme ?? Theme.System) == Theme.Dark);
    }

    private void UpdateTrayMenuTheme(bool isDark)
    {
        if (_trayIcon?.ContextMenuStrip is null) return;

        _trayIcon.ContextMenuStrip.Renderer = new CustomMenuRenderer(isDark);

        if (isDark)
        {
            _trayIcon.ContextMenuStrip.BackColor = System.Drawing.Color.FromArgb(255, 37, 37, 38);
            _trayIcon.ContextMenuStrip.ForeColor = System.Drawing.Color.FromArgb(255, 224, 224, 224);
        }
        else
        {
            _trayIcon.ContextMenuStrip.BackColor = System.Drawing.Color.White;
            _trayIcon.ContextMenuStrip.ForeColor = System.Drawing.Color.FromArgb(255, 30, 30, 30);
        }
    }

    private static System.Drawing.Icon GetOrCreateTrayIcon()
    {
        try
        {
            var resStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/app.ico"))?.Stream;
            if (resStream != null)
            {
                return new System.Drawing.Icon(resStream, 16, 16);
            }
        }
        catch { }

        try
        {
            var resStream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/netspeedimg.png"))?.Stream;
            if (resStream != null)
            {
                using var srcBmp = new System.Drawing.Bitmap(resStream);
                using var targetBmp = new System.Drawing.Bitmap(16, 16);
                using (var g = System.Drawing.Graphics.FromImage(targetBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.DrawImage(srcBmp, 0, 0, 16, 16);
                }
                IntPtr hIcon = targetBmp.GetHicon();
                return System.Drawing.Icon.FromHandle(hIcon);
            }
        }
        catch { }

        try
        {
            string localIco = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.ico");
            if (File.Exists(localIco))
            {
                return new System.Drawing.Icon(localIco, 16, 16);
            }

            string localPng = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "netspeedimg.png");
            if (File.Exists(localPng))
            {
                using var srcBmp = new System.Drawing.Bitmap(localPng);
                using var targetBmp = new System.Drawing.Bitmap(16, 16);
                using (var g = System.Drawing.Graphics.FromImage(targetBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.DrawImage(srcBmp, 0, 0, 16, 16);
                }
                IntPtr hIcon = targetBmp.GetHicon();
                return System.Drawing.Icon.FromHandle(hIcon);
            }
        }
        catch { }

        return System.Drawing.SystemIcons.Application;
    }

    private async void OnSpeedUpdated((double downBps, double upBps) speed)
    {
        if (_prefs is null) return;

        string down = SpeedConverter.Format(speed.downBps, _prefs.DisplayUnit);
        string up = SpeedConverter.Format(speed.upBps, _prefs.DisplayUnit);

        await Dispatcher.InvokeAsync(() =>
        {
            if (_trayIcon is not null)
                _trayIcon.Text = $"NetTrafficMonitor\n↓ {down}\n↑ {up}";
        });
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow is not { IsVisible: true })
        {
            _settingsWindow = new MainWindow(_monitor!, _prefs!, _conn!);
            _settingsWindow.Show();
        }

        _settingsWindow.Activate();
    }

    public void ToggleHud()
    {
        if (_hudWindow is { IsVisible: true })
        {
            _hudWindow.Hide();
            _hudWindow = null;
            _prefs!.HudEnabled = false;
        }
        else
        {
            _hudWindow = new HudWindow(_monitor!, _prefs!);
            _hudWindow.Show();
            _prefs!.HudEnabled = true;
        }
    }

    private async Task ShutdownAsync()
    {
        _monitor?.Stop();
        _prefs?.SaveAsync(_conn!).Wait(2000);
        _trayIcon?.Dispose();
        _conn?.Close();
        _conn?.Dispose();

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _monitor?.Dispose();
        base.OnExit(e);
    }

    private class CustomMenuRenderer : ToolStripRenderer
    {
        private readonly bool _isDark;

        public CustomMenuRenderer(bool isDark)
        {
            _isDark = isDark;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                var rc = new System.Drawing.Rectangle(0, 0, e.Item.Width, e.Item.Height);
                using var b = new System.Drawing.SolidBrush(_isDark ? System.Drawing.Color.FromArgb(255, 62, 62, 66) : System.Drawing.Color.FromArgb(255, 229, 229, 229));
                e.Graphics.FillRectangle(b, rc);
            }
            else
            {
                base.OnRenderMenuItemBackground(e);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var rc = new System.Drawing.Rectangle(32, e.Item.Height / 2, e.Item.Width - 32, 1);
            using var b = new System.Drawing.SolidBrush(_isDark ? System.Drawing.Color.FromArgb(255, 85, 85, 85) : System.Drawing.Color.FromArgb(255, 215, 215, 215));
            e.Graphics.FillRectangle(b, rc);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = _isDark ? System.Drawing.Color.FromArgb(255, 224, 224, 224) : System.Drawing.Color.FromArgb(255, 30, 30, 30);
            base.OnRenderItemText(e);
        }
    }
}
