using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Data.Sqlite;
using NetTrafficMonitor.Core.Data;
using NetTrafficMonitor.Core.Models;
using NetTrafficMonitor.Core.Services;
using NetTrafficMonitor.Service;

namespace NetTrafficMonitor.ViewModels;

public partial class SettingsViewModel : INotifyPropertyChanged
{
    private readonly NetworkMonitorService _monitor;
    private readonly UserPreferences _prefs;
    private readonly SqliteConnection _conn;
    private readonly AdapterRepository _adapterRepo;
    private readonly DataUsageAggregator _aggregator;

    public SettingsViewModel(NetworkMonitorService monitor, UserPreferences prefs, SqliteConnection conn)
    {
        _monitor = monitor;
        _prefs = prefs;
        _conn = conn;
        _adapterRepo = new AdapterRepository(conn);
        _aggregator = new DataUsageAggregator(conn);

        _speedUnits = new ObservableCollection<SpeedUnit>(Enum.GetValues<SpeedUnit>());
        _selectedUnit = _prefs.DisplayUnit;

        _selectedTheme = _prefs.Theme;

        _dataSizeUnits = new ObservableCollection<DataSizeUnit>(Enum.GetValues<DataSizeUnit>());
        _selectedDataSizeUnit = _prefs.DataUsageDisplayUnit;

        _startDate = DateTime.Today;
        _endDate = DateTime.Today;

        _adapters = new ObservableCollection<NetworkAdapter>();
        _selectedAdapter = null;

        LoadAdaptersCommand = new AsyncRelayCommand(async () => await LoadAdaptersAsync());
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync());
        RefreshUsageCommand = new AsyncRelayCommand(async () => await RefreshUsageAsync());

        _ = LoadAdaptersAsync();
        _ = RefreshUsageAsync();
    }

    public ObservableCollection<SpeedUnit> SpeedUnits => _speedUnits;
    private readonly ObservableCollection<SpeedUnit> _speedUnits;

    public SpeedUnit SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            _selectedUnit = value;
            _prefs.DisplayUnit = value;
            _prefs.NotifyPreferencesChanged();
            OnPropertyChanged();
        }
    }
    private SpeedUnit _selectedUnit;

    public ObservableCollection<DataSizeUnit> DataSizeUnits => _dataSizeUnits;
    private readonly ObservableCollection<DataSizeUnit> _dataSizeUnits;

    public DataSizeUnit SelectedDataSizeUnit
    {
        get => _selectedDataSizeUnit;
        set
        {
            _selectedDataSizeUnit = value;
            _prefs.DataUsageDisplayUnit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PeriodFormattedDownload));
            OnPropertyChanged(nameof(PeriodFormattedUpload));
        }
    }
    private DataSizeUnit _selectedDataSizeUnit;

    public ObservableCollection<DailyUsage> DailyUsages => _dailyUsages;
    private readonly ObservableCollection<DailyUsage> _dailyUsages = new();

    public ObservableCollection<NetworkAdapter> Adapters => _adapters;
    private readonly ObservableCollection<NetworkAdapter> _adapters;

    public NetworkAdapter? SelectedAdapter
    {
        get => _selectedAdapter;
        set
        {
            _selectedAdapter = value;
            OnPropertyChanged();
        }
    }
    private NetworkAdapter? _selectedAdapter;

    public ObservableCollection<Theme> Themes => _themes;
    private readonly ObservableCollection<Theme> _themes = new() { Theme.Dark, Theme.Light, Theme.System };

    public Theme SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme == value) return;
            _selectedTheme = value;
            _prefs.Theme = value;
            OnPropertyChanged();
            App.ApplyTheme(value);
        }
    }
    private Theme _selectedTheme;

    public string FontFamily
    {
        get => _prefs.FontFamily;
        set
        {
            _prefs.FontFamily = value;
            OnPropertyChanged();
        }
    }

    public double FontSize
    {
        get => _prefs.FontSize;
        set
        {
            _prefs.FontSize = value;
            _prefs.NotifyPreferencesChanged();
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => _prefs.StartMinimized;
        set
        {
            _prefs.StartMinimized = value;
            OnPropertyChanged();
        }
    }

    public bool MinimizeToTray
    {
        get => _prefs.MinimizeToTray;
        set
        {
            _prefs.MinimizeToTray = value;
            OnPropertyChanged();
        }
    }

    public bool RunOnStartup
    {
        get => _prefs.RunOnStartup;
        set
        {
            _prefs.RunOnStartup = value;
            OnPropertyChanged();
        }
    }

    public bool HudEnabled
    {
        get => _prefs.HudEnabled;
        set
        {
            _prefs.HudEnabled = value;
            OnPropertyChanged();
        }
    }

    public double HudOpacity
    {
        get => _prefs.HudOpacity;
        set
        {
            _prefs.HudOpacity = value;
            _prefs.NotifyPreferencesChanged();
            OnPropertyChanged();
        }
    }

    public DateTime StartDate
    {
        get => _startDate;
        set
        {
            if (value > EndDate) value = EndDate;
            _startDate = value;
            OnPropertyChanged();
            _ = RefreshUsageAsync();
        }
    }
    private DateTime _startDate;

    public DateTime EndDate
    {
        get => _endDate;
        set
        {
            if (value > DateTime.Today) value = DateTime.Today;
            if (value < StartDate) value = StartDate;
            _endDate = value;
            OnPropertyChanged();
            _ = RefreshUsageAsync();
        }
    }
    private DateTime _endDate;

    public string PeriodFormattedDownload => DataSizeConverter.Format(PeriodDownloadBytes, SelectedDataSizeUnit);
    public string PeriodFormattedUpload => DataSizeConverter.Format(PeriodUploadBytes, SelectedDataSizeUnit);

    public long PeriodDownloadBytes { get; private set; }
    public long PeriodUploadBytes { get; private set; }

    public ICommand LoadAdaptersCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RefreshUsageCommand { get; }

    private async Task LoadAdaptersAsync()
    {
        var adapters = await _monitor.RefreshAdaptersAsync();
        _adapters.Clear();
        foreach (var a in adapters)
        {
            _adapters.Add(a);
            if (a.IsSelected) _selectedAdapter = a;
        }
        OnPropertyChanged(nameof(SelectedAdapter));
    }

    private async Task SaveAsync()
    {
        _prefs.DisplayUnit = _selectedUnit;
        _prefs.DataUsageDisplayUnit = _selectedDataSizeUnit;
        if (_selectedAdapter != null)
        {
            await _monitor.SelectAdapterAsync(_selectedAdapter.Id);
            _prefs.SelectedAdapterId = _selectedAdapter.Id;
        }
        await _prefs.SaveAsync(_conn);
        App.ApplyTheme(_prefs.Theme);
    }

    private async Task RefreshUsageAsync()
    {
        int adapterId = _selectedAdapter?.Id ?? _monitor.CurrentAdapterId;
        if (adapterId <= 0) return;

        var start = _startDate.Date;
        var end = _endDate.Date.AddDays(1).AddTicks(-1);

        PeriodDownloadBytes = await _aggregator.GetBytesDownloadedAsync(adapterId, DataPeriod.Custom, start, end);
        PeriodUploadBytes = await _aggregator.GetBytesUploadedAsync(adapterId, DataPeriod.Custom, start, end);
        OnPropertyChanged(nameof(PeriodDownloadBytes));
        OnPropertyChanged(nameof(PeriodUploadBytes));
        OnPropertyChanged(nameof(PeriodFormattedDownload));
        OnPropertyChanged(nameof(PeriodFormattedUpload));

        var daily = await _aggregator.GetDailyUsageAsync(adapterId, start, end);
        _dailyUsages.Clear();
        foreach (var d in daily) _dailyUsages.Add(d);
        
        OnPropertyChanged(nameof(DailyUsages));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<Task> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (_isExecuting) return;
        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
