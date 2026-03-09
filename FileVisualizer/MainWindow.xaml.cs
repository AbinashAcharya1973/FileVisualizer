using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace FileVisualizer
{
    public partial class MainWindow : Window
    {
        private const double CellWidth = 12.0;
        private const double RowHeight = 28.0;  // Height per row

        private List<FileEventRow> _currentRows = new();
        private DateTime _currentStart;
        private DateTime _currentEnd;

        public MainWindow()
        {
            InitializeComponent();
            DpFrom.SelectedDate = DateTime.Today;
            DpTo.SelectedDate = DateTime.Today;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select folder to visualize",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                TxtFolder.Text = dlg.FolderName;
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtFolder.Text) || !Directory.Exists(TxtFolder.Text))
            {
                MessageBox.Show("Please select a valid folder.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fromDate = DpFrom.SelectedDate ?? DateTime.Today;
            var toDate = DpTo.SelectedDate ?? DateTime.Today;

            if (toDate < fromDate)
            {
                MessageBox.Show("'To' date must be same or after 'From' date.", "Invalid Date Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string mode = ((ComboBoxItem)CmbEventMode.SelectedItem).Content.ToString() ?? "";

            var files = Directory.GetFiles(TxtFolder.Text).OrderBy(p => p).ToList();

            if (files.Count == 0)
            {
                MessageBox.Show("No files in selected folder.", "Empty Folder", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var start = fromDate.Date;
            var end = toDate.Date.AddDays(1).AddTicks(-1);

            var fileEvents = new List<FileEventRow>();
            int totalEvents = 0;

            foreach (var f in files)
            {
                try
                {
                    var info = new FileInfo(f);
                    var created = info.CreationTime;
                    var modified = info.LastWriteTime;

                    var events = new List<FileEvent>();

                    if (mode.Contains("Created") && created >= start && created <= end)
                    {
                        events.Add(new FileEvent { Time = created, Type = EventType.Created });
                        totalEvents++;
                    }

                    if (mode.Contains("Modified") && modified >= start && modified <= end)
                    {
                        events.Add(new FileEvent { Time = modified, Type = EventType.Modified });
                        totalEvents++;
                    }

                    fileEvents.Add(new FileEventRow
                    {
                        FilePath = f,
                        FileName = System.IO.Path.GetFileName(f),
                        Events = events,
                        CreationTime = created,
                        ModifiedTime = modified
                    });
                }
                catch { }
            }

            if (fileEvents.Count == 0)
            {
                MessageBox.Show("No files found.", "No Files", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            TxtStatus.Text = $"📁 {fileEvents.Count} files  •  📊 {totalEvents} events  •  📅 {fromDate:dd MMM} to {toDate:dd MMM yyyy}";

            _currentRows = fileEvents;
            _currentStart = start;
            _currentEnd = end;

            DrawFileList(fileEvents);
            DrawTimeMap(fileEvents, start, end);
            ScrollToFirstEvent(fileEvents, start);
        }

        private void DrawFileList(List<FileEventRow> rows)
        {
            FileNamesCanvas.Children.Clear();
            double canvasHeight = rows.Count * RowHeight;
            FileNamesCanvas.Height = canvasHeight;

            for (int i = 0; i < rows.Count; i++)
            {
                double y = i * RowHeight;

                // Row background (alternating)
                var rowBg = new Rectangle
                {
                    Width = 220,
                    Height = RowHeight,
                    Fill = i % 2 == 0
                        ? new SolidColorBrush(Color.FromRgb(13, 17, 23))
                        : new SolidColorBrush(Color.FromRgb(22, 27, 34))
                };
                Canvas.SetLeft(rowBg, 0);
                Canvas.SetTop(rowBg, y);
                FileNamesCanvas.Children.Add(rowBg);

                // Row border
                var rowLine = new Line
                {
                    X1 = 0,
                    Y1 = y + RowHeight,
                    X2 = 220,
                    Y2 = y + RowHeight,
                    Stroke = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                    StrokeThickness = 1
                };
                FileNamesCanvas.Children.Add(rowLine);

                // File name text
                var fileName = new TextBlock
                {
                    Text = rows[i].FileName,
                    Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI"),
                    MaxWidth = 200,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = rows[i].FileName
                };
                Canvas.SetLeft(fileName, 12);
                Canvas.SetTop(fileName, y + (RowHeight - 16) / 2);  // Center vertically
                FileNamesCanvas.Children.Add(fileName);
            }
        }

        private void ScrollToFirstEvent(List<FileEventRow> rows, DateTime start)
        {
            var firstEvent = rows.SelectMany(r => r.Events).OrderBy(e => e.Time).FirstOrDefault();
            if (firstEvent != null)
            {
                int minuteOffset = (int)(firstEvent.Time - start).TotalMinutes;
                double scrollX = Math.Max(0, (minuteOffset - 60) * CellWidth);
                MapScroll.ScrollToHorizontalOffset(scrollX);
            }
        }

        private void DrawTimeMap(List<FileEventRow> rows, DateTime startDate, DateTime endDate)
        {
            MapCanvas.Children.Clear();
            TimeHeaderCanvas.Children.Clear();

            int totalMinutes = (int)Math.Ceiling((endDate - startDate).TotalMinutes);
            if (totalMinutes <= 0) totalMinutes = 1440;

            double mapWidth = totalMinutes * CellWidth;
            double mapHeight = rows.Count * RowHeight;

            MapCanvas.Width = mapWidth;
            MapCanvas.Height = Math.Max(mapHeight, 400);
            TimeHeaderCanvas.Width = mapWidth;

            DrawDayNightBackground(startDate, totalMinutes, Math.Max(mapHeight, 600));
            DrawGridLines(rows.Count, totalMinutes, Math.Max(mapHeight, 600));
            DrawTimeHeader(startDate, totalMinutes);
            DrawFileRows(rows, startDate, totalMinutes);
        }

        private void DrawDayNightBackground(DateTime start, int totalMinutes, double height)
        {
            for (int m = 0; m < totalMinutes; m += 60)
            {
                var hourTime = start.AddMinutes(m);
                double x = m * CellWidth;
                double width = Math.Min(60, totalMinutes - m) * CellWidth;

                var bgRect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = GetTimeOfDayBrush(hourTime.Hour),
                    StrokeThickness = 0
                };

                Canvas.SetLeft(bgRect, x);
                Canvas.SetTop(bgRect, 0);
                Panel.SetZIndex(bgRect, -100);
                MapCanvas.Children.Add(bgRect);
            }
        }

        private static Brush GetTimeOfDayBrush(int hour)
        {
            if (hour >= 22 || hour < 5)
                return new SolidColorBrush(Color.FromRgb(1, 4, 9));
            else if (hour == 5)
                return new SolidColorBrush(Color.FromRgb(10, 15, 25));
            else if (hour == 6)
                return new SolidColorBrush(Color.FromRgb(30, 25, 35));
            else if (hour == 7)
                return new SolidColorBrush(Color.FromRgb(45, 40, 45));
            else if (hour >= 8 && hour < 10)
                return new SolidColorBrush(Color.FromRgb(20, 30, 45));
            else if (hour >= 10 && hour < 16)
                return new SolidColorBrush(Color.FromRgb(13, 25, 40));
            else if (hour >= 16 && hour < 18)
                return new SolidColorBrush(Color.FromRgb(20, 28, 42));
            else if (hour == 18)
                return new SolidColorBrush(Color.FromRgb(35, 25, 40));
            else if (hour == 19)
                return new SolidColorBrush(Color.FromRgb(25, 18, 35));
            else if (hour == 20)
                return new SolidColorBrush(Color.FromRgb(15, 12, 25));
            else
                return new SolidColorBrush(Color.FromRgb(8, 8, 18));
        }

        private void DrawGridLines(int rowCount, int totalMinutes, double height)
        {
            // Vertical hour lines
            for (int m = 0; m <= totalMinutes; m += 60)
            {
                double x = m * CellWidth;
                var line = new Line
                {
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                    StrokeThickness = 1
                };
                Panel.SetZIndex(line, -50);
                MapCanvas.Children.Add(line);
            }

            // Horizontal row lines - must match file list exactly
            for (int r = 0; r <= rowCount; r++)
            {
                double y = r * RowHeight;
                var line = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = totalMinutes * CellWidth,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                    StrokeThickness = 1
                };
                Panel.SetZIndex(line, -50);
                MapCanvas.Children.Add(line);
            }

            // Alternating row backgrounds for better visibility
            for (int r = 0; r < rowCount; r++)
            {
                if (r % 2 == 1)
                {
                    double y = r * RowHeight;
                    var rowBg = new Rectangle
                    {
                        Width = totalMinutes * CellWidth,
                        Height = RowHeight,
                        Fill = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
                    };
                    Canvas.SetLeft(rowBg, 0);
                    Canvas.SetTop(rowBg, y);
                    Panel.SetZIndex(rowBg, -80);
                    MapCanvas.Children.Add(rowBg);
                }
            }
        }

        private void DrawTimeHeader(DateTime start, int totalMinutes)
        {
            var currentDate = start.Date;
            var endDate = start.AddMinutes(totalMinutes);

            // Date labels
            while (currentDate <= endDate)
            {
                int minuteOffset = (int)(currentDate - start).TotalMinutes;
                if (minuteOffset >= 0 && minuteOffset < totalMinutes)
                {
                    double x = minuteOffset * CellWidth;

                    var dateBg = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(35, 134, 54)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 2, 8, 2),
                        Child = new TextBlock
                        {
                            Text = currentDate.ToString("ddd, dd MMM"),
                            Foreground = Brushes.White,
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold
                        }
                    };
                    Canvas.SetLeft(dateBg, x + 5);
                    Canvas.SetTop(dateBg, 4);
                    TimeHeaderCanvas.Children.Add(dateBg);
                }
                currentDate = currentDate.AddDays(1);
            }

            // Hour labels
            for (int m = 0; m < totalMinutes; m += 60)
            {
                var time = start.AddMinutes(m);
                double x = m * CellWidth;

                if (time.Hour == 0) continue;

                var hourLabel = new TextBlock
                {
                    Text = time.ToString("h tt").ToLower(),
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                    FontSize = 10
                };
                Canvas.SetLeft(hourLabel, x + 4);
                Canvas.SetTop(hourLabel, 26);
                TimeHeaderCanvas.Children.Add(hourLabel);

                var tick = new Line
                {
                    X1 = x,
                    Y1 = 40,
                    X2 = x,
                    Y2 = 44,
                    Stroke = new SolidColorBrush(Color.FromRgb(72, 79, 88)),
                    StrokeThickness = 1
                };
                TimeHeaderCanvas.Children.Add(tick);
            }
        }

        private void DrawFileRows(List<FileEventRow> rows, DateTime start, int totalMinutes)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                double y = r * RowHeight;

                foreach (var evt in row.Events)
                {
                    int minuteIndex = (int)Math.Floor((evt.Time - start).TotalMinutes);
                    if (minuteIndex < 0 || minuteIndex >= totalMinutes) continue;

                    double x = minuteIndex * CellWidth;

                    var tile = new Rectangle
                    {
                        Width = CellWidth - 2,
                        Height = RowHeight - 6,
                        RadiusX = 3,
                        RadiusY = 3,
                        Fill = evt.Type == EventType.Created
                            ? new SolidColorBrush(Color.FromRgb(57, 211, 83))
                            : new SolidColorBrush(Color.FromRgb(9, 105, 218)),
                        Cursor = Cursors.Hand
                    };

                    tile.Effect = new DropShadowEffect
                    {
                        Color = evt.Type == EventType.Created ? Colors.LimeGreen : Colors.DodgerBlue,
                        BlurRadius = 10,
                        ShadowDepth = 0,
                        Opacity = 0.7
                    };

                    Canvas.SetLeft(tile, x + 1);
                    Canvas.SetTop(tile, y + 3);  // Center in row
                    Panel.SetZIndex(tile, 100);

                    // Tooltip
                    var tooltipPanel = new StackPanel { Margin = new Thickness(4) };
                    tooltipPanel.Children.Add(new TextBlock
                    {
                        Text = row.FileName,
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        Foreground = Brushes.White
                    });
                    tooltipPanel.Children.Add(new TextBlock
                    {
                        Text = $"{evt.Type}: {evt.Time:dddd, dd MMM yyyy}",
                        Foreground = Brushes.LightGray,
                        Margin = new Thickness(0, 4, 0, 0)
                    });
                    tooltipPanel.Children.Add(new TextBlock
                    {
                        Text = $"Time: {evt.Time:hh:mm:ss tt}",
                        Foreground = Brushes.LightGray
                    });

                    tile.ToolTip = new ToolTip
                    {
                        Content = tooltipPanel,
                        Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61))
                    };

                    // Click handler
                    string filePath = row.FilePath;
                    DateTime created = row.CreationTime;
                    DateTime modified = row.ModifiedTime;

                    tile.MouseLeftButtonDown += (s, e) =>
                    {
                        var result = MessageBox.Show(
                            $"📁 {System.IO.Path.GetFileName(filePath)}\n\n" +
                            $"📍 {filePath}\n\n" +
                            $"Created: {created:G}\n" +
                            $"Modified: {modified:G}\n\n" +
                            $"Open this file?",
                            "File Details",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (result == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Failed to open: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };

                    MapCanvas.Children.Add(tile);
                }
            }
        }

        private void MapScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Sync file list vertical scroll
            if (Math.Abs(e.VerticalChange) > 0.1)
            {
                FileNamesScroll.ScrollToVerticalOffset(e.VerticalOffset);
            }

            // Sync time header horizontal scroll
            if (Math.Abs(e.HorizontalChange) > 0.1)
            {
                var transform = TimeHeaderCanvas.RenderTransform as TranslateTransform;
                if (transform == null)
                {
                    transform = new TranslateTransform();
                    TimeHeaderCanvas.RenderTransform = transform;
                }
                transform.X = -e.HorizontalOffset;
            }
        }
    }

    public enum EventType { Created, Modified }

    public class FileEvent
    {
        public DateTime Time { get; set; }
        public EventType Type { get; set; }
    }

    public class FileEventRow
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public List<FileEvent> Events { get; set; } = new();
        public DateTime CreationTime { get; set; }
        public DateTime ModifiedTime { get; set; }
    }
}