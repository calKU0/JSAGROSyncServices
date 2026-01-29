using Microsoft.Extensions.Configuration;
using ServiceManager.Enums;
using ServiceManager.Helpers;
using ServiceManager.Models;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConfigurationManager = System.Configuration.ConfigurationManager;

namespace ServiceManager
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<LogFileItem> logFiles = new ObservableCollection<LogFileItem>();
        private DispatcherTimer refreshTimer;
        private ServiceController _serviceController;
        private readonly object _serviceLock = new();
        private FileSystemWatcher _logWatcher;
        private readonly DispatcherTimer _logReloadDebounce;
        public ObservableCollection<ServiceItem> AvailableServices { get; } = new ObservableCollection<ServiceItem>();
        private ServiceItem? _selectedService;
        private const int InitialTailLines = 2000;
        private const int PageLines = 1000;

        private readonly BulkObservableCollection<LogLine> _currentLogLines = new();
        private BulkObservableCollection<LogLine> _filteredLogLines = new();
        private string? _currentPath;
        private long _loadedStartOffset = 0;
        private bool _isLoadingMore = false;
        private bool _reachedFileStart = false;
        private long _lastReadOffset = 0;
        private object _lastSelectedLog;
        private bool _isAtBottom = true;
        private readonly List<(TextBox Length, TextBox Width, TextBox Height, TextBox Weight, TextBox Name)> _deliveryTextBoxes = new();
        private List<Delivery> _deliveries = new();

        public MainWindow()
        {
            InitializeComponent();
            IcAvailableServices.ItemsSource = AvailableServices;

            _logReloadDebounce = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _logReloadDebounce.Tick += async (_, _) =>
            {
                _logReloadDebounce.Stop();
                await LoadLogFilesAsync();
            };
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
            _logWatcher.Created += (_, _) => Dispatcher.InvokeAsync(TriggerLogReloadDebounced);
            _logWatcher.Deleted += (_, _) => Dispatcher.InvokeAsync(TriggerLogReloadDebounced);
        }

        private void TriggerLogReloadDebounced()
        {
            _logReloadDebounce.Stop();
            _logReloadDebounce.Start();
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
            _serviceController?.Close();
            _serviceController?.Dispose();
            _serviceController = new ServiceController(service.ServiceName);

            InitLogWatcher();
            _ = RefreshServiceStatusAsync();
            _ = LoadLogFilesAsync();
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

        private async void BtnShowLogs_Click(object sender, RoutedEventArgs e)
        {
            MainContentArea.Visibility = Visibility.Visible;
            LogsViewContainer.Visibility = Visibility.Visible;
            ConfigViewContainer.Visibility = Visibility.Collapsed;

            LvLogFiles.ItemsSource = logFiles;
            IcLogLines.ItemsSource = _filteredLogLines;

            HookLogLinesScrollViewer();

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

            await LoadLogFilesAsync();
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
            await RefreshServiceStatusAsync();

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
                    ApplyFilter();

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

        private IConfigurationRoot LoadAppSettings(string path)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(path) ?? ".")
                .AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: true);

            return builder.Build();
        }

        private void SaveAppSettings(string path, Dictionary<string, string> values)
        {
            var json = File.ReadAllText(path);
            var jsonObject = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

            foreach (var kvp in values)
            {
                var parts = kvp.Key.Split(':');
                JsonObject current = jsonObject;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (current[parts[i]] == null || current[parts[i]].GetType() != typeof(JsonObject))
                        current[parts[i]] = new JsonObject();
                    current = current[parts[i]].AsObject();
                }

                current.Remove(parts[^1]);

                if (kvp.Value.StartsWith("{") || kvp.Value.StartsWith("["))
                {
                    current[parts[^1]] = JsonNode.Parse(kvp.Value);
                }
                else
                {
                    current[parts[^1]] = JsonValue.Create(kvp.Value);
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(path, jsonObject.ToJsonString(options));
        }

        private void LoadConfig()
        {
            if (_selectedService == null) return;

            try
            {
                var config = LoadAppSettings(_selectedService.ExternalConfigPath);

                ConfigStackPanel.Children.Clear();

                var groupedFields = ConfigFieldDefinitions.AllFields.GroupBy(f => f.Group);

                foreach (var group in groupedFields)
                {
                    // Keep only fields that exist in JSON
                    var existingFields = group
                        .Where(f => config.GetSection(f.Key).Exists())
                        .ToList();

                    if (!existingFields.Any())
                        continue;

                    var groupBox = new GroupBox { Header = group.Key, Margin = new Thickness(0, 6, 0, 6) };
                    var groupPanel = new StackPanel { Margin = new Thickness(6) };

                    foreach (var field in existingFields)
                    {
                        string value = config[field.Key] ?? "";

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
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(295) });
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
                LoadDeliveries(config);

                // Save button
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

        private void LoadDeliveries(IConfiguration config)
        {
            _deliveryTextBoxes.Clear();

            var section = config.GetSection("AppSettings:Deliveries");
            if (!section.Exists())
                return;

            _deliveries = section.Get<List<Delivery>>() ?? new List<Delivery>();

            var groupBox = new GroupBox
            {
                Header = "Dostawy",
                Margin = new Thickness(0, 6, 0, 6)
            };

            var panel = new StackPanel { Margin = new Thickness(6) };

            // Header
            var header = new Grid();
            for (int i = 0; i < 6; i++)
                header.ColumnDefinitions.Add(new ColumnDefinition());

            AddHeader(header, "Długość", 0);
            AddHeader(header, "Szerokość", 1);
            AddHeader(header, "Wysokość", 2);
            AddHeader(header, "Waga", 3);
            AddHeader(header, "Nazwa", 4);

            panel.Children.Add(header);

            foreach (var d in _deliveries)
                AddDeliveryRow(panel, d);

            var addBtn = new Button
            {
                Content = "Dodaj dostawę",
                Margin = new Thickness(0, 6, 0, 0)
            };

            addBtn.Click += (_, _) =>
            {
                // insert before the "Add" button (which is last child)
                int insertIndex = panel.Children.Count - 1;
                AddDeliveryRow(panel, new Delivery(), insertIndex);
            };

            panel.Children.Add(addBtn);

            groupBox.Content = panel;
            ConfigStackPanel.Children.Add(groupBox);
        }

        private static void AddHeader(Grid grid, string text, int col)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2)
            };
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private void AddDeliveryRow(StackPanel panel, Delivery d, int? insertIndex = null)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };

            for (int i = 0; i < 6; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());

            var lengthBox = new TextBox { Text = d.Length.ToString(), Margin = new Thickness(2) };
            var widthBox = new TextBox { Text = d.Width.ToString(), Margin = new Thickness(2) };
            var heightBox = new TextBox { Text = d.Height.ToString(), Margin = new Thickness(2) };
            var weightBox = new TextBox { Text = d.Weight.ToString(CultureInfo.InvariantCulture), Margin = new Thickness(2) };
            var nameBox = new TextBox { Text = d.DeliveryName, Margin = new Thickness(2) };

            Grid.SetColumn(lengthBox, 0);
            Grid.SetColumn(widthBox, 1);
            Grid.SetColumn(heightBox, 2);
            Grid.SetColumn(weightBox, 3);
            Grid.SetColumn(nameBox, 4);

            var removeBtn = new Button
            {
                Content = "✖",
                Foreground = Brushes.Red,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            removeBtn.Click += (_, _) =>
            {
                panel.Children.Remove(grid);
                _deliveryTextBoxes.Remove((lengthBox, widthBox, heightBox, weightBox, nameBox));
            };

            Grid.SetColumn(removeBtn, 5);

            grid.Children.Add(lengthBox);
            grid.Children.Add(widthBox);
            grid.Children.Add(heightBox);
            grid.Children.Add(weightBox);
            grid.Children.Add(nameBox);
            grid.Children.Add(removeBtn);

            if (insertIndex.HasValue)
                panel.Children.Insert(insertIndex.Value, grid);
            else
                panel.Children.Add(grid);

            _deliveryTextBoxes.Add((lengthBox, widthBox, heightBox, weightBox, nameBox));
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
                var valuesToSave = new Dictionary<string, string>();
                var errors = new List<string>();

                // ---- normal fields ----
                foreach (var grid in ConfigStackPanel.Children.OfType<GroupBox>()
                             .SelectMany(gb => ((StackPanel)gb.Content).Children.OfType<Grid>()))
                {
                    var tb = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (tb != null && tb.Tag is string key)
                    {
                        var fieldDef = ConfigFieldDefinitions.AllFields.FirstOrDefault(f => f.Key == key);
                        var value = tb.Text.Trim();

                        if (fieldDef != null)
                        {
                            switch (fieldDef.FieldType)
                            {
                                case ConfigFieldType.Int:
                                    if (!int.TryParse(value, out _))
                                        errors.Add($"Pole „{fieldDef.Label}” wymaga liczby całkowitej.");
                                    break;

                                case ConfigFieldType.Decimal:
                                    if (!TryParseDecimal(value, out var decValue))
                                        errors.Add($"Pole „{fieldDef.Label}” wymaga liczby dziesiętnej (np. 12.5).");
                                    else
                                        value = decValue.ToString(CultureInfo.InvariantCulture);
                                    break;
                            }
                        }

                        valuesToSave[key] = value;
                    }
                }

                // ---- deliveries validation & build ----
                var deliveries = new List<Delivery>();

                foreach (var t in _deliveryTextBoxes)
                {
                    if (!int.TryParse(t.Length.Text, out var length) ||
                        !int.TryParse(t.Width.Text, out var width) ||
                        !int.TryParse(t.Height.Text, out var height) ||
                        !TryParseDecimal(t.Weight.Text, out var weight))
                    {
                        errors.Add("Wymiary i waga muszą być poprawnymi liczbami.");
                        continue;
                    }

                    if (length <= 0 || width <= 0 || height <= 0 || weight <= 0)
                        errors.Add("Wymiary i waga muszą być większe od zera.");

                    if (string.IsNullOrWhiteSpace(t.Name.Text))
                        errors.Add("Nazwa dostawy nie może być pusta.");

                    deliveries.Add(new Delivery
                    {
                        Length = length,
                        Width = width,
                        Height = height,
                        Weight = weight,
                        DeliveryName = t.Name.Text.Trim()
                    });
                }

                if (errors.Any())
                {
                    MessageBox.Show(
                        string.Join("\n", errors),
                        "Błąd walidacji",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // ---- save everything ONCE ----
                valuesToSave["AppSettings:Deliveries"] =
                    JsonSerializer.Serialize(deliveries);

                SaveAppSettings(_selectedService.ExternalConfigPath, valuesToSave);

                MessageBox.Show(
                    "Konfiguracja zapisana.",
                    "Info",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Nie udało się zapisać konfiguracji: {ex.Message}",
                    "Błąd",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task LoadLogFilesAsync()
        {
            logFiles.Clear();
            if (_selectedService == null || !Directory.Exists(_selectedService.LogFolderPath)) return;

            try
            {
                var files = await Task.Run(() =>
                {
                    return Directory.GetFiles(_selectedService.LogFolderPath, "*.txt")
                        .Select(filePath =>
                        {
                            int warnings = 0;
                            int errors = 0;

                            try
                            {
                                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                using (var sr = new StreamReader(fs))
                                {
                                    string? line;
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
                                if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, DateTimeStyles.None, out DateTime dt))
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
                                    Date = parsedDate ?? DateTime.MinValue
                                };
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(f => f != null)
                        .OrderByDescending(f => f!.Date)
                        .ToList();
                });

                logFiles.Clear();
                foreach (var f in files)
                    logFiles.Add(f);

                // Auto-select latest file when none chosen
                if (logFiles.Count > 0 && LvLogFiles.SelectedItem == null)
                {
                    LvLogFiles.SelectedItem = logFiles[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się załadować listy plików logów: {ex.Message}");
            }
        }

        private async Task LoadEntireFileWithFilterAsync(LogFileItem item)
        {
            _currentLogLines.Clear();
            _isAtBottom = true;
            _currentPath = item.Path;

            var info = new FileInfo(item.Path);
            _lastReadOffset = info.Length;
            _loadedStartOffset = 0;
            _reachedFileStart = true;

            string[] allLines = Array.Empty<string>();

            await Task.Run(() =>
            {
                using var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);

                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }

                allLines = lines.ToArray();
            });

            var filteredLines = allLines
                .Select(ParseLogLine)
                .Where(l => l.Level == LogLevel.Error || l.Level == LogLevel.Warning)
                .ToList();

            _currentLogLines.AddRange(filteredLines);

            ApplyFilter();

            await Dispatcher.BeginInvoke(() =>
            {
                if (IcLogLines.Items.Count > 0)
                {
                    IcLogLines.UpdateLayout();
                    IcLogLines.ScrollIntoView(IcLogLines.Items[^1]);
                }
            }, DispatcherPriority.Background);
        }

        private void ApplyFilter()
        {
            _filteredLogLines.Clear();

            bool filter = ChkShowOnlyWarningsAndErrors.IsChecked == true;

            foreach (var line in _currentLogLines)
            {
                if (!filter || line.Level == LogLevel.Warning || line.Level == LogLevel.Error)
                    _filteredLogLines.Add(line);
            }

            if (_filteredLogLines.Count > 0 && _isAtBottom)
                IcLogLines.ScrollIntoView(_filteredLogLines[^1]);
        }

        private async void ChkShowOnlyWarningsAndErrors_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkShowOnlyWarningsAndErrors.IsChecked == true)
            {
                if (LvLogFiles.SelectedItem is LogFileItem item)
                {
                    await LoadEntireFileWithFilterAsync(item);
                }
            }
            else
            {
                await LoadSelectedFileContentAsync();
            }
        }

        private void IcLogLines_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var sv = e.OriginalSource as ScrollViewer;
            if (sv != null)
            {
                _isAtBottom = sv.VerticalOffset >= sv.ScrollableHeight - 1;
            }

            if (e.VerticalOffset <= 2)
                _ = LoadMoreAsync();
        }

        private void HookLogLinesScrollViewer()
        {
            // Use Dispatcher to ensure layout is ready
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var sv = GetScrollViewer(IcLogLines);
                if (sv != null)
                {
                    sv.ScrollChanged -= IcLogLines_ScrollChanged; // prevent double hook
                    sv.ScrollChanged += IcLogLines_ScrollChanged;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private ScrollViewer? GetScrollViewer(DependencyObject dep)
        {
            if (dep is ScrollViewer viewer)
                return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = VisualTreeHelper.GetChild(dep, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        private async Task LoadSelectedFileContentAsync()
        {
            _currentLogLines.Clear();
            _isAtBottom = true;

            if (LvLogFiles.SelectedItem is not LogFileItem item || !File.Exists(item.Path))
                return;

            _currentPath = item.Path;

            if (ChkShowOnlyWarningsAndErrors.IsChecked == true)
            {
                await LoadEntireFileWithFilterAsync(item);
                return;
            }

            try
            {
                _lastReadOffset = new FileInfo(item.Path).Length;

                var (lines, startOffset, reachedStart) =
                    await Task.Run(() => LogFileReader.ReadLastLines(item.Path, InitialTailLines));

                _loadedStartOffset = startOffset;
                _reachedFileStart = reachedStart;

                _currentLogLines.AddRange(lines.Select(ParseLogLine));
                ApplyFilter();

                await Dispatcher.BeginInvoke(() =>
                {
                    if (IcLogLines.Items.Count > 0 && _isAtBottom)
                    {
                        IcLogLines.UpdateLayout();
                        IcLogLines.ScrollIntoView(IcLogLines.Items[^1]);
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
                    ApplyFilter();
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

        private async void LvLogFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
            await LoadSelectedFileContentAsync();
        }

        private async Task RefreshServiceStatusAsync()
        {
            if (_serviceController == null) return;

            try
            {
                var status = await Task.Run(() =>
                {
                    lock (_serviceLock)
                    {
                        _serviceController.Refresh();
                        return _serviceController.Status;
                    }
                });

                ApplyServiceStatus(status);
            }
            catch (Exception ex)
            {
                ApplyServiceError(ex);
            }
        }

        private void ApplyServiceStatus(ServiceControllerStatus status)
        {
            switch (status)
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
                    ServiceStatusText.Text = status.ToString();
                    BtnStartService.IsEnabled = false;
                    BtnStopService.IsEnabled = false;
                    BtnRestartService.IsEnabled = false;
                    break;
            }
        }

        private void ApplyServiceError(Exception ex)
        {
            ServiceStatusDot.Fill = Brushes.Gray;
            ServiceStatusText.Text = "Error";
            BtnStartService.IsEnabled = BtnStopService.IsEnabled = BtnRestartService.IsEnabled = false;
            MessageBox.Show($"Nie udało się sprawdzić statusu usługi: {ex.Message}");
        }

        private void SetServiceButtonsTemporarilyEnabled(bool isEnabled)
        {
            BtnStartService.IsEnabled = isEnabled;
            BtnStopService.IsEnabled = isEnabled;
            BtnRestartService.IsEnabled = isEnabled;
        }

        private async Task RunServiceOperationAsync(Action<ServiceController> operation)
        {
            if (_serviceController == null) return;

            SetServiceButtonsTemporarilyEnabled(false);

            try
            {
                await Task.Run(() =>
                {
                    lock (_serviceLock)
                    {
                        operation(_serviceController);
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Błąd usługi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                await RefreshServiceStatusAsync();
            }
        }

        private async void BtnStartService_Click(object sender, RoutedEventArgs e)
        {
            if (_serviceController == null) return;

            await RunServiceOperationAsync(sc =>
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            });
        }

        private async void BtnStopService_Click(object sender, RoutedEventArgs e)
        {
            if (_serviceController == null) return;

            await RunServiceOperationAsync(sc =>
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            });
        }

        private async void BtnRestartService_Click(object sender, RoutedEventArgs e)
        {
            if (_serviceController == null) return;

            await RunServiceOperationAsync(sc =>
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
            });
        }
        private static bool TryParseDecimal(string input, out decimal result)
        {

            var normalized = input.Replace(',', '.');
            return decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out result);
        }
    }
}