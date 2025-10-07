using ServiceManager.Enums;
using ServiceManager.Models;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Specialized;
using ServiceManager.Helpers;

namespace ServiceManager
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<LogFileItem> logFiles = new ObservableCollection<LogFileItem>();
        private DispatcherTimer refreshTimer;
        private ServiceController _serviceController;
        private FileSystemWatcher _logWatcher;
        public ObservableCollection<ServiceItem> AvailableServices { get; } = new ObservableCollection<ServiceItem>();
        private ServiceItem? _selectedService;
        private const int InitialTailLines = 2000;
        private const int PageLines = 1000;

        private readonly BulkObservableCollection<LogLine> _currentLogLines = new();
        private string? _currentPath;
        private long _loadedStartOffset = 0;
        private bool _isLoadingMore = false;
        private bool _reachedFileStart = false;
        private long _lastReadOffset = 0;
        private object _lastSelectedLog;

        public MainWindow()
        {
            InitializeComponent();
            IcAvailableServices.ItemsSource = AvailableServices;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadAvailableServices();

            if (AvailableServices.Count == 1)
            {
                SelectService(AvailableServices[0]);
                ServiceSelectionOverlay.Visibility = Visibility.Collapsed;
                MainContentAreaNav.Visibility = Visibility.Visible;
            }
            else
            {
                ServiceSelectionOverlay.Visibility = Visibility.Visible;
                MainContentAreaNav.Visibility = Visibility.Collapsed;
            }
        }

        private void InitLogWatcher()
        {
            // Dispose previous watcher if it exists
            if (_logWatcher != null)
            {
                _logWatcher.EnableRaisingEvents = false;
                _logWatcher.Created -= (s, e) => Dispatcher.Invoke(LoadLogFiles);
                _logWatcher.Deleted -= (s, e) => Dispatcher.Invoke(LoadLogFiles);
                _logWatcher.Dispose();
                _logWatcher = null;
            }

            if (string.IsNullOrEmpty(_selectedService?.LogFolderPath) || !Directory.Exists(_selectedService.LogFolderPath))
                return;

            _logWatcher = new FileSystemWatcher(_selectedService.LogFolderPath, "*.txt")
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            _logWatcher.Created += (s, e) => Dispatcher.Invoke(LoadLogFiles);
            _logWatcher.Deleted += (s, e) => Dispatcher.Invoke(LoadLogFiles);
        }

        private void LoadAvailableServices()
        {
            AvailableServices.Clear();

            var keys = ConfigurationManager.AppSettings.AllKeys
                .Where(k => k.StartsWith("Service_"))
                .Select(k => k.Split('_')[1]) // "Allegro", "Erli"
                .Distinct();

            foreach (var key in keys)
            {
                var service = new ServiceItem
                {
                    Id = key,
                    Name = ConfigurationManager.AppSettings[$"Service_{key}_Name"] ?? key,
                    LogoPath = ConfigurationManager.AppSettings[$"Service_{key}_LogoPath"] ?? "",
                    ServiceName = ConfigurationManager.AppSettings[$"Service_{key}_ServiceName"] ?? "",
                    LogFolderPath = ConfigurationManager.AppSettings[$"Service_{key}_LogFolder"] ?? "",
                    ExternalConfigPath = ConfigurationManager.AppSettings[$"Service_{key}_ConfigPath"] ?? ""
                };
                AvailableServices.Add(service);
            }

            CbServiceSelector.ItemsSource = AvailableServices;
        }

        private void SelectService(ServiceItem service)
        {
            if (service == null) return;

            _selectedService = service;
            _serviceController = new ServiceController(service.ServiceName);

            InitLogWatcher();
            RefreshServiceStatus();
            LoadLogFiles();
            LoadConfig();
            _currentLogLines.Clear();
            ServiceNameTextBox.Text = service.Name;
        }

        private void ServiceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ServiceItem service)
            {
                SelectService(service);
                CbServiceSelector.SelectedValue = service.Id;
                ServiceSelectionOverlay.Visibility = Visibility.Collapsed;
                CbServiceSelector.Visibility = Visibility.Visible;
                MainContentAreaNav.Visibility = Visibility.Visible;
            }
        }

        private void CbServiceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbServiceSelector.SelectedItem is ServiceItem selected)
            {
                SelectService(selected);

                // Make sure UI panels update correctly
                ServiceSelectionOverlay.Visibility = Visibility.Collapsed;
                CbServiceSelector.Visibility = Visibility.Visible;
                MainContentAreaNav.Visibility = Visibility.Visible;

                MainContentArea.Visibility = Visibility.Collapsed;
                LogsViewContainer.Visibility = Visibility.Collapsed;
                ConfigViewContainer.Visibility = Visibility.Collapsed;
                BtnShowLogs.IsChecked = false;
                BtnShowConfig.IsChecked = false;
            }
        }

        private void BtnShowLogs_Click(object sender, RoutedEventArgs e)
        {
            MainContentArea.Visibility = Visibility.Visible;
            LogsViewContainer.Visibility = Visibility.Visible;
            ConfigViewContainer.Visibility = Visibility.Collapsed;

            LvLogFiles.ItemsSource = logFiles;
            IcLogLines.ItemsSource = _currentLogLines;

            if (refreshTimer != null)
            {
                refreshTimer.Stop();
                refreshTimer.Tick -= RefreshTimer_Tick;
            }
            refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            refreshTimer.Tick += RefreshTimer_Tick;
            refreshTimer.Start();

            LoadLogFiles();
        }

        private void BtnShowConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
            MainContentArea.Visibility = Visibility.Visible;
            LogsViewContainer.Visibility = Visibility.Collapsed;
            ConfigViewContainer.Visibility = Visibility.Visible;
        }

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshServiceStatus();

            if (LogsViewContainer.Visibility != Visibility.Visible ||
                LvLogFiles.SelectedItem is not LogFileItem item ||
                string.IsNullOrEmpty(_currentPath)) return;

            var listBox = IcLogLines;
            if (listBox.Items.Count == 0) return;

            // Check if user is at bottom
            var sv = FindVisualChilds.FindVisualChild<ScrollViewer>(listBox);
            bool isAtBottom = sv != null &&
                              Math.Abs(sv.VerticalOffset - sv.ScrollableHeight) < 2;

            try
            {
                var newLines = await Task.Run(() => LogFileReader.ReadNewLines(_currentPath!, ref _lastReadOffset));
                if (newLines.Count > 0)
                {
                    // update in-memory log lines
                    _currentLogLines.AddRange(newLines.Select(ParseLogLine));

                    // update warning/error counters
                    int newWarnings = newLines.Count(l => l.Contains("WRN]", StringComparison.Ordinal));
                    int newErrors = newLines.Count(l => l.Contains("ERR]", StringComparison.Ordinal));

                    item.WarningsCount += newWarnings;
                    item.ErrorsCount += newErrors;

                    // scroll to bottom if user was at bottom
                    if (isAtBottom && sv != null)
                    {
                        await Dispatcher.BeginInvoke(() =>
                        {
                            listBox.ScrollIntoView(listBox.Items[^1]);
                        }, DispatcherPriority.Background);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd odczytu logu {item.Name}: {ex.Message}");
            }
        }

        private void LoadConfig()
        {
            if (_selectedService == null) return;

            try
            {
                var map = new ExeConfigurationFileMap { ExeConfigFilename = _selectedService.ExternalConfigPath };
                var config = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);

                ConfigStackPanel.Children.Clear();

                var groupedFields = ConfigFieldDefinitions.AllFields.GroupBy(f => f.Group);

                foreach (var group in groupedFields)
                {
                    // Keep only fields that exist in config
                    var existingFields = group.Where(f => config.AppSettings.Settings.AllKeys.Contains(f.Key)).ToList();

                    if (!existingFields.Any())
                        continue; // skip empty groups

                    var groupBox = new GroupBox { Header = group.Key, Margin = new Thickness(0, 6, 0, 6) };
                    var groupPanel = new StackPanel { Margin = new Thickness(6) };

                    foreach (var field in existingFields)
                    {
                        string value = config.AppSettings.Settings[field.Key]?.Value ?? "";

                        var label = new TextBlock
                        {
                            Text = field.Label,
                            Margin = new Thickness(0, 4, 0, 4),
                            VerticalAlignment = VerticalAlignment.Center,
                            ToolTip = string.IsNullOrEmpty(field.Description) ? null : field.Description
                        };

                        var textbox = new TextBox
                        {
                            Text = value,
                            Margin = new Thickness(0, 4, 0, 4),
                            IsEnabled = field.IsEnabled,
                            Tag = field.Key,
                            AcceptsReturn = field.Key == "AllegroSafetyMeasures",
                            Height = field.Key == "AllegroSafetyMeasures" ? 120 : Double.NaN,
                            TextWrapping = TextWrapping.Wrap
                        };

                        var grid = new Grid();
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                        Grid.SetColumn(label, 0);
                        Grid.SetColumn(textbox, 1);

                        grid.Children.Add(label);
                        grid.Children.Add(textbox);

                        groupPanel.Children.Add(grid);
                    }

                    groupBox.Content = groupPanel;
                    ConfigStackPanel.Children.Add(groupBox);
                }

                var saveButton = new Button
                {
                    Content = "Zapisz",
                    Margin = new Thickness(6, 12, 6, 24),
                    Padding = new Thickness(12, 6, 12, 6),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xE2)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0x7A, 0xBD)),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = 120
                };

                saveButton.Click += BtnSaveConfig_Click;

                ConfigStackPanel.Children.Add(saveButton);

                ConfigViewContainer.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się załadować konfiguracji: {ex.Message}");
            }
        }

        private void BtnReloadConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedService == null) return;

            try
            {
                var map = new ExeConfigurationFileMap { ExeConfigFilename = _selectedService.ExternalConfigPath };
                var config = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);

                foreach (var grid in ConfigStackPanel.Children.OfType<GroupBox>()
                             .SelectMany(gb => ((StackPanel)gb.Content).Children.OfType<Grid>()))
                {
                    var tb = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (tb != null && tb.Tag is string key)
                    {
                        string value = tb.Text;
                        if (config.AppSettings.Settings[key] != null)
                            config.AppSettings.Settings[key].Value = value;
                        else
                            config.AppSettings.Settings.Add(key, value);
                    }
                }

                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
                MessageBox.Show("Konfiguracja zapisana.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się zapisać konfiguracji: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadLogFiles()
        {
            logFiles.Clear();
            if (!Directory.Exists(_selectedService.LogFolderPath)) return;

            try
            {
                var files = Directory.GetFiles(_selectedService.LogFolderPath, "*.txt")
                    .Select(filePath =>
                    {
                        int warnings = 0;
                        int errors = 0;

                        try
                        {
                            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var sr = new StreamReader(fs))
                            {
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    if (line.Contains("WRN]", StringComparison.Ordinal)) warnings++;
                                    if (line.Contains("ERR]", StringComparison.Ordinal)) errors++;
                                }
                            }

                            string fileName = Path.GetFileNameWithoutExtension(filePath);
                            string datePart = fileName.Replace("log-", "");

                            string formattedDate = fileName;
                            DateTime? parsedDate = null;
                            if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                            {
                                formattedDate = dt.ToString("dd.MM.yyyy");
                                parsedDate = dt;
                            }

                            return new LogFileItem
                            {
                                Name = formattedDate,
                                Path = filePath,
                                WarningsCount = warnings,
                                ErrorsCount = errors,
                                Date = parsedDate ?? DateTime.MinValue // add Date property in LogFileItem
                            };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(f => f != null)
                    .OrderByDescending(f => f.Date) // latest first
                    .ToList();

                foreach (var f in files)
                    logFiles.Add(f);

                // Auto-select latest file
                if (logFiles.Count > 0)
                {
                    LvLogFiles.SelectedItem = logFiles[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się załadować listy plików logów: {ex.Message}");
            }
        }

        private void IcLogLines_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // np. doładowywanie przy dojściu do góry
            if (e.VerticalOffset <= 0)
                _ = LoadMoreAsync();
        }

        private async void LoadSelectedFileContent()
        {
            _currentLogLines.Clear();
            if (LvLogFiles.SelectedItem is not LogFileItem item || !File.Exists(item.Path))
                return;

            _currentPath = item.Path;
            try
            {
                _lastReadOffset = new FileInfo(item.Path).Length;

                var (lines, startOffset, reachedStart) =
                    await Task.Run(() => LogFileReader.ReadLastLines(item.Path, InitialTailLines));

                _loadedStartOffset = startOffset;
                _reachedFileStart = reachedStart;

                _currentLogLines.AddRange(lines.Select(ParseLogLine));

                await Dispatcher.BeginInvoke(() =>
                {
                    if (IcLogLines.Items.Count > 0)
                    {
                        IcLogLines.UpdateLayout();
                        IcLogLines.ScrollIntoView(IcLogLines.Items[^1]); // bottom
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd odczytu logu {item.Name}: {ex.Message}");
            }
        }

        private async Task LoadMoreAsync()
        {
            if (_isLoadingMore || _reachedFileStart || string.IsNullOrEmpty(_currentPath)) return;
            _isLoadingMore = true;

            try
            {
                var anchor = IcLogLines.Items.Count > 0 ? IcLogLines.Items[0] : null;

                var (older, newStart, reachedStart) =
                    await Task.Run(() => LogFileReader.ReadPreviousLines(_currentPath!, _loadedStartOffset, PageLines));

                if (older.Count > 0)
                {
                    _currentLogLines.InsertRange(0, older.Select(ParseLogLine));
                    _loadedStartOffset = newStart;
                    _reachedFileStart = reachedStart;

                    if (anchor != null)
                    {
                        IcLogLines.UpdateLayout();
                        IcLogLines.ScrollIntoView(anchor); // keep position
                    }
                }
            }
            finally
            {
                _isLoadingMore = false;
            }
        }

        private LogLine ParseLogLine(string line)
        {
            var level = LogLevel.Information;
            if (line.Contains("ERR]", StringComparison.Ordinal)) level = LogLevel.Error;
            else if (line.Contains("WRN]", StringComparison.Ordinal)) level = LogLevel.Warning;
            return new LogLine { Level = level, Message = line };
        }

        private void LvLogFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LvLogFiles.SelectedItem == null)
            {
                if (_lastSelectedLog != null)
                {
                    LvLogFiles.SelectedItem = _lastSelectedLog;
                }
                return;
            }

            _lastSelectedLog = LvLogFiles.SelectedItem;

            TxtSelectedFileName.Text = ((LogFileItem)LvLogFiles.SelectedItem).Name;
            LoadSelectedFileContent();
        }

        private void RefreshServiceStatus()
        {
            try
            {
                _serviceController.Refresh();

                switch (_serviceController.Status)
                {
                    case ServiceControllerStatus.Running:
                        ServiceStatusDot.Fill = Brushes.Green;
                        ServiceStatusText.Text = "Online";
                        BtnStartService.IsEnabled = false;
                        BtnStopService.IsEnabled = true;
                        BtnRestartService.IsEnabled = true;
                        break;

                    case ServiceControllerStatus.Stopped:
                        ServiceStatusDot.Fill = Brushes.Red;
                        ServiceStatusText.Text = "Offline";
                        BtnStartService.IsEnabled = true;
                        BtnStopService.IsEnabled = false;
                        BtnRestartService.IsEnabled = false;
                        break;

                    case ServiceControllerStatus.Paused:
                        ServiceStatusDot.Fill = Brushes.Orange;
                        ServiceStatusText.Text = "Paused";
                        BtnStartService.IsEnabled = true;
                        BtnStopService.IsEnabled = true;
                        BtnRestartService.IsEnabled = true;
                        break;

                    default: // Pending states
                        ServiceStatusDot.Fill = Brushes.Gray;
                        ServiceStatusText.Text = _serviceController.Status.ToString();
                        BtnStartService.IsEnabled = false;
                        BtnStopService.IsEnabled = false;
                        BtnRestartService.IsEnabled = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                ServiceStatusDot.Fill = Brushes.Gray;
                ServiceStatusText.Text = "Error";
                BtnStartService.IsEnabled = BtnStopService.IsEnabled = BtnRestartService.IsEnabled = false;
                MessageBox.Show($"Nie udało się sprawdzić statusu usługi: {ex.Message}");
            }
        }

        private void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _serviceController.Start();
                _serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd przy uruchamianiu usługi: {ex.Message}");
            }
            RefreshServiceStatus();
        }

        private void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _serviceController.Stop();
                _serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd przy zatrzymywaniu usługi: {ex.Message}");
            }
            RefreshServiceStatus();
        }

        private void BtnRestartService_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _serviceController.Stop();
                _serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));

                _serviceController.Start();
                _serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Błąd przy restartowaniu usługi: {ex.Message}");
            }
            RefreshServiceStatus();
        }
    }
}