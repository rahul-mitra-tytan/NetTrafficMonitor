using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;
using NetTrafficMonitor.Core.Models;
using NetTrafficMonitor.Service;
using NetTrafficMonitor.ViewModels;

namespace NetTrafficMonitor.Views;

public partial class MainWindow : Window
{
    private readonly SettingsViewModel _vm;

    public MainWindow(NetworkMonitorService monitor, UserPreferences prefs, SqliteConnection conn)
    {
        InitializeComponent();
        _vm = new SettingsViewModel(monitor, prefs, conn);
        DataContext = _vm;
        _vm.PropertyChanged += Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.DailyUsages))
        {
            Dispatcher.InvokeAsync(DrawGraph);
        }
    }

    private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawGraph();
    }

    private void DrawGraph()
    {
        GraphCanvas.Children.Clear();
        if (GraphCanvas.ActualWidth == 0 || GraphCanvas.ActualHeight == 0) return;
        
        double w = GraphCanvas.ActualWidth;
        double h = GraphCanvas.ActualHeight;

        double marginX = 65; // For Y axis labels
        double marginY = 25; // For X axis labels
        double marginYTop = 15; // Padding at top so text isn't clipped
        
        double graphW = w - marginX;
        double graphH = h - marginY;

        var gridPen = new System.Windows.Media.Pen((System.Windows.Media.Brush)FindResource("BorderBrush"), 1);
        
        // Draw bottom axis line
        var lineX = new Line { X1 = marginX, Y1 = graphH, X2 = w, Y2 = graphH, Stroke = gridPen.Brush, StrokeThickness = 1 };
        GraphCanvas.Children.Add(lineX);
        
        // Draw left axis line
        var lineYAxis = new Line { X1 = marginX, Y1 = marginYTop, X2 = marginX, Y2 = graphH, Stroke = gridPen.Brush, StrokeThickness = 1 };
        GraphCanvas.Children.Add(lineYAxis);

        if (_vm.DailyUsages == null || !_vm.DailyUsages.Any()) return;

        var start = _vm.StartDate.Date;
        var end = _vm.EndDate.Date;
        int totalDays = (int)(end - start).TotalDays + 1;
        
        // Aim for at least 30 pixels per bucket so labels don't bunch
        int maxPoints = Math.Max(1, (int)(graphW / 30));
        
        int bucketSize = 1;
        if (totalDays > maxPoints)
        {
            bucketSize = (int)Math.Ceiling((double)totalDays / maxPoints);
        }

        int numBuckets = (int)Math.Ceiling((double)totalDays / bucketSize);
        if (numBuckets <= 0) return;
        
        var buckets = new List<(long Down, long Up, DateTime Date)>(new (long, long, DateTime)[numBuckets]);

        foreach (var usage in _vm.DailyUsages)
        {
            if (usage.Date < start || usage.Date > end) continue;
            int dayOffset = (int)(usage.Date - start).TotalDays;
            int bucketIdx = dayOffset / bucketSize;
            if (bucketIdx >= 0 && bucketIdx < numBuckets)
            {
                var existing = buckets[bucketIdx];
                var newDate = existing.Date == default ? usage.Date : existing.Date;
                buckets[bucketIdx] = (existing.Down + usage.Download, existing.Up + usage.Upload, newDate);
            }
        }

        // Fill missing dates
        for (int i = 0; i < numBuckets; i++) 
        {
            if (buckets[i].Date == default) buckets[i] = (buckets[i].Down, buckets[i].Up, start.AddDays(i * bucketSize));
        }

        long maxVal = 0;
        foreach (var b in buckets)
        {
            maxVal = Math.Max(maxVal, b.Down);
            maxVal = Math.Max(maxVal, b.Up);
        }
        if (maxVal == 0) maxVal = 1;

        // Draw Y axis labels
        int yAxisTicks = 4;
        double usableH = graphH - marginYTop;
        for (int i = 0; i <= yAxisTicks; i++)
        {
            long val = (long)(maxVal * ((double)i / yAxisTicks));
            double y = graphH - (usableH * ((double)i / yAxisTicks));
            
            var tickLine = new Line { X1 = marginX, Y1 = y, X2 = w, Y2 = y, Stroke = gridPen.Brush, StrokeThickness = 1, Opacity = 0.3 };
            GraphCanvas.Children.Add(tickLine);

            var tb = new TextBlock
            {
                Text = NetTrafficMonitor.Core.Services.DataSizeConverter.Format(val, _vm.SelectedDataSizeUnit),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                FontSize = 10,
                TextAlignment = TextAlignment.Right,
                Width = marginX - 10
            };
            Canvas.SetLeft(tb, 0);
            // Ensure we don't go above 0
            Canvas.SetTop(tb, Math.Max(0, y - 7));
            GraphCanvas.Children.Add(tb);
        }

        var downPoints = new System.Windows.Media.PointCollection();
        var upPoints = new System.Windows.Media.PointCollection();

        for (int i = 0; i < numBuckets; i++)
        {
            double x = marginX + (numBuckets == 1 ? graphW / 2 : (graphW / (numBuckets - 1)) * i);
            double yDown = graphH - ((double)buckets[i].Down / maxVal) * usableH;
            double yUp = graphH - ((double)buckets[i].Up / maxVal) * usableH;
            
            downPoints.Add(new System.Windows.Point(x, yDown));
            upPoints.Add(new System.Windows.Point(x, yUp));
            
            // X-axis label (only draw if enough space or at specific intervals)
            if (numBuckets <= 8 || i % (numBuckets / 6 + 1) == 0 || i == numBuckets - 1)
            {
                var tb = new TextBlock
                {
                    Text = buckets[i].Date.ToString("MMM dd"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center
                };
                tb.Measure(new System.Windows.Size(Double.PositiveInfinity, Double.PositiveInfinity));
                Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
                Canvas.SetTop(tb, graphH + 5);
                GraphCanvas.Children.Add(tb);
            }
        }

        var downLine = new Polyline
        {
            Points = downPoints,
            Stroke = (System.Windows.Media.Brush)FindResource("GreenBrush"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        var upLine = new Polyline
        {
            Points = upPoints,
            Stroke = (System.Windows.Media.Brush)FindResource("OrangeBrush"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };

        GraphCanvas.Children.Add(downLine);
        GraphCanvas.Children.Add(upLine);

        // Add invisible hit-test strips for ToolTips
        double stripWidth = numBuckets > 1 ? graphW / (numBuckets - 1) : graphW;
        for (int i = 0; i < numBuckets; i++)
        {
            double x = marginX + (numBuckets == 1 ? graphW / 2 : (graphW / (numBuckets - 1)) * i);
            var strip = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0)), // Nearly invisible but solid for hit test
                Width = Math.Max(15, stripWidth),
                Height = h,
                ToolTip = new System.Windows.Controls.ToolTip
                {
                    Content = $"Date: {buckets[i].Date:MMM dd, yyyy}\nDownload: {NetTrafficMonitor.Core.Services.DataSizeConverter.Format(buckets[i].Down, _vm.SelectedDataSizeUnit)}\nUpload: {NetTrafficMonitor.Core.Services.DataSizeConverter.Format(buckets[i].Up, _vm.SelectedDataSizeUnit)}",
                    Background = (System.Windows.Media.Brush)FindResource("ControlBgBrush"),
                    Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                }
            };
            Canvas.SetLeft(strip, x - strip.Width / 2);
            Canvas.SetTop(strip, 0);
            GraphCanvas.Children.Add(strip);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        if (app.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
