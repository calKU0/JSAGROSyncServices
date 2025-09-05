using JSAGROAllegroServiceConfiguration.Enums;
using JSAGROAllegroServiceConfiguration.Models;
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
using JSAGROServiceManager.Helpers;

namespace JSAGROServiceManager
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<LogFileItem> logFiles = new ObservableCollection<LogFileItem>();
        private DispatcherTimer refreshTimer;
        private ServiceController _serviceController;
        private FileSystemWatcher _logWatcher;

        private readonly string _externalConfigPath = ConfigurationManager.AppSettings["ExternalConfigPath"].ToString();
        private readonly string _logFolderPath = ConfigurationManager.AppSettings["LogFolderPath"].ToString();
        private readonly string _serviceName = ConfigurationManager.AppSettings["ServiceName"].ToString();

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
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serviceController = new ServiceController(_serviceName);
            IcLogLines.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(IcLogLines_ScrollChanged),
                handledEventsToo: true);
            RefreshServiceStatus();
            InitLogWatcher();
        }

        private void InitLogWatcher()
        {
            _logWatcher = new FileSystemWatcher(_logFolderPath, "*.txt")
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
            _logWatcher.Created += (s, e) => Dispatcher.Invoke(LoadLogFiles);
            _logWatcher.Deleted += (s, e) => Dispatcher.Invoke(LoadLogFiles);
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
            try
            {
                var map = new ExeConfigurationFileMap { ExeConfigFilename = _externalConfigPath };
                Configuration externalConfig = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);

                // Gaska API
                TxtGaskaApiBaseUrl.Text = externalConfig.AppSettings.Settings["GaskaApiBaseUrl"]?.Value ?? "";
                TxtGaskaApiAcronym.Text = externalConfig.AppSettings.Settings["GaskaApiAcronym"]?.Value ?? "";
                TxtGaskaApiPerson.Text = externalConfig.AppSettings.Settings["GaskaApiPerson"]?.Value ?? "";
                TxtGaskaApiPassword.Text = externalConfig.AppSettings.Settings["GaskaApiPassword"]?.Value ?? "";
                TxtGaskaApiKey.Text = externalConfig.AppSettings.Settings["GaskaApiKey"]?.Value ?? "";
                TxtGaskaApiProductsPerPage.Text = externalConfig.AppSettings.Settings["GaskaApiProductsPerPage"]?.Value ?? "";
                TxtGaskaApiProductsInterval.Text = externalConfig.AppSettings.Settings["GaskaApiProductsInterval"]?.Value ?? "";
                TxtGaskaApiProductPerDay.Text = externalConfig.AppSettings.Settings["GaskaApiProductPerDay"]?.Value ?? "";
                TxtGaskaApiProductInterval.Text = externalConfig.AppSettings.Settings["GaskaApiProductInterval"]?.Value ?? "";

                // Allegro API
                TxtAllegroApiBaseUrl.Text = externalConfig.AppSettings.Settings["AllegroApiBaseUrl"]?.Value ?? "";
                TxtAllegroAuthBaseUrl.Text = externalConfig.AppSettings.Settings["AllegroAuthBaseUrl"]?.Value ?? "";
                TxtAllegroClientName.Text = externalConfig.AppSettings.Settings["AllegroClientName"]?.Value ?? "";
                TxtAllegroClientId.Text = externalConfig.AppSettings.Settings["AllegroClientId"]?.Value ?? "";
                TxtAllegroClientSecret.Text = externalConfig.AppSettings.Settings["AllegroClientSecret"]?.Value ?? "";
                TxtAllegroScope.Text = externalConfig.AppSettings.Settings["AllegroScope"]?.Value ?? "";

                // Other settings
                TxtGaskaCategoriesId.Text = externalConfig.AppSettings.Settings["GaskaCategoriesId"]?.Value ?? "";
                TxtLogsExpirationDays.Text = externalConfig.AppSettings.Settings["LogsExpirationDays"]?.Value ?? "";
                TxtFetchIntervalMinutes.Text = externalConfig.AppSettings.Settings["FetchIntervalMinutes"]?.Value ?? "";
                TxtOwnMarginPercent.Text = externalConfig.AppSettings.Settings["OwnMarginPercent"]?.Value ?? "";
                TxtAllegroMarginUnder5PLN.Text = externalConfig.AppSettings.Settings["AllegroMarginUnder5PLN"]?.Value ?? "";
                TxtAllegroMarginBetween5and1000PLNPercent.Text = externalConfig.AppSettings.Settings["AllegroMarginBetween5and1000PLNPercent"]?.Value ?? "";
                TxtAllegroMarginMoreThan1000PLN.Text = externalConfig.AppSettings.Settings["AllegroMarginMoreThan1000PLN"]?.Value ?? "";
                TxtAddPLNToBulkyProducts.Text = externalConfig.AppSettings.Settings["AddPLNToBulkyProducts"]?.Value ?? "";
                TxtAddPLNToCustomProducts.Text = externalConfig.AppSettings.Settings["AddPLNToCustomProducts"]?.Value ?? "";
                TxtAllegroDeliveryName.Text = externalConfig.AppSettings.Settings["AllegroDeliveryName"]?.Value ?? "";
                TxtAllegroHandlingTime.Text = externalConfig.AppSettings.Settings["AllegroHandlingTime"]?.Value ?? "";
                TxtAllegroHandlingTimeCustomProducts.Text = externalConfig.AppSettings.Settings["AllegroHandlingTimeCustomProducts"]?.Value ?? "";
                TxtAllegroSafetyMeasures.Text = (externalConfig.AppSettings.Settings["AllegroSafetyMeasures"]?.Value ?? "").Replace("\\r\\n", Environment.NewLine);
                TxtAllegroWarranty.Text = externalConfig.AppSettings.Settings["AllegroWarranty"]?.Value ?? "";
                TxtAllegroReturnPolicy.Text = externalConfig.AppSettings.Settings["AllegroReturnPolicy"]?.Value ?? "";
                TxtAllegroImpliedWarranty.Text = externalConfig.AppSettings.Settings["AllegroImpliedWarranty"]?.Value ?? "";
                TxtAllegroResponsiblePerson.Text = externalConfig.AppSettings.Settings["AllegroResponsiblePerson"]?.Value ?? "";
                TxtAllegroResponsibleProducer.Text = externalConfig.AppSettings.Settings["AllegroResponsibleProducer"]?.Value ?? "";
            }
            catch (ConfigurationErrorsException confEx)
            {
                MessageBox.Show($"Błąd w pliku konfiguracyjnym: {confEx.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się załadować konfiguracji: {ex.Message}");
            }
        }

        private void LoadLogFiles()
        {
            logFiles.Clear();
            if (!Directory.Exists(_logFolderPath)) return;

            try
            {
                var files = Directory.GetFiles(_logFolderPath, "*.txt")
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

        private void BtnReloadConfig_Click(object sender, RoutedEventArgs e)
        {
            LoadConfig();
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // --- Walidacja ---
                var errors = new List<string>();

                // decimal
                if (!decimal.TryParse(TxtAllegroMarginMoreThan1000PLN.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errors.Add("'Marża allegro powyżej 1000 PLN (w PLN)' musi być liczbą dziesiętną.");

                if (!decimal.TryParse(TxtAddPLNToBulkyProducts.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errors.Add("'Dodatek PLN do towarów gabarytowych (żółty samochodzik)' musi być liczbą dziesiętną.");

                if (!decimal.TryParse(TxtAddPLNToCustomProducts.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errors.Add("'Dodatek PLN do towarów niestandardowych (czerwony samochodzik)' musi być liczbą dziesiętną.");

                if (!decimal.TryParse(TxtAllegroMarginBetween5and1000PLNPercent.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errors.Add("'Marża allegro 5-1000 PLN (w %)' musi być liczbą dziesiętną.");

                if (!decimal.TryParse(TxtAllegroMarginUnder5PLN.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errors.Add("'Marża allegro poniżej 5 PLN (w PLN)' musi być liczbą dziesiętną.");

                if (!decimal.TryParse(TxtOwnMarginPercent.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                    errors.Add("'Własna marża (%)' musi być liczbą dziesiętną.");

                // int
                if (!int.TryParse(TxtFetchIntervalMinutes.Text, out _))
                    errors.Add("'Co ile odświeżać stany/ceny' musi być liczbą całkowitą.");
                if (!int.TryParse(TxtLogsExpirationDays.Text, out _))
                    errors.Add("'Ilość dni zachowania logów' musi być liczbą całkowitą.");

                if (errors.Any())
                {
                    MessageBox.Show(
                        "Błędy walidacji:\n" + string.Join("\n", errors),
                        "Walidacja",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                var map = new ExeConfigurationFileMap { ExeConfigFilename = _externalConfigPath };
                Configuration externalConfig = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);

                // --- Gaska API ---
                externalConfig.AppSettings.Settings["GaskaApiBaseUrl"].Value = TxtGaskaApiBaseUrl.Text;
                externalConfig.AppSettings.Settings["GaskaApiAcronym"].Value = TxtGaskaApiAcronym.Text;
                externalConfig.AppSettings.Settings["GaskaApiPerson"].Value = TxtGaskaApiPerson.Text;
                externalConfig.AppSettings.Settings["GaskaApiPassword"].Value = TxtGaskaApiPassword.Text;
                externalConfig.AppSettings.Settings["GaskaApiKey"].Value = TxtGaskaApiKey.Text;
                externalConfig.AppSettings.Settings["GaskaApiProductsPerPage"].Value = TxtGaskaApiProductsPerPage.Text;
                externalConfig.AppSettings.Settings["GaskaApiProductsInterval"].Value = TxtGaskaApiProductsInterval.Text;
                externalConfig.AppSettings.Settings["GaskaApiProductPerDay"].Value = TxtGaskaApiProductPerDay.Text;
                externalConfig.AppSettings.Settings["GaskaApiProductInterval"].Value = TxtGaskaApiProductInterval.Text;

                // --- Allegro API ---
                externalConfig.AppSettings.Settings["AllegroApiBaseUrl"].Value = TxtAllegroApiBaseUrl.Text;
                externalConfig.AppSettings.Settings["AllegroAuthBaseUrl"].Value = TxtAllegroAuthBaseUrl.Text;
                externalConfig.AppSettings.Settings["AllegroClientName"].Value = TxtAllegroClientName.Text;
                externalConfig.AppSettings.Settings["AllegroClientId"].Value = TxtAllegroClientId.Text;
                externalConfig.AppSettings.Settings["AllegroClientSecret"].Value = TxtAllegroClientSecret.Text;
                externalConfig.AppSettings.Settings["AllegroScope"].Value = TxtAllegroScope.Text;

                // --- Other settings ---
                externalConfig.AppSettings.Settings["GaskaCategoriesId"].Value = TxtGaskaCategoriesId.Text;
                externalConfig.AppSettings.Settings["LogsExpirationDays"].Value = TxtLogsExpirationDays.Text;
                externalConfig.AppSettings.Settings["FetchIntervalMinutes"].Value = TxtFetchIntervalMinutes.Text;
                externalConfig.AppSettings.Settings["OwnMarginPercent"].Value = TxtOwnMarginPercent.Text;
                externalConfig.AppSettings.Settings["AllegroMarginUnder5PLN"].Value = TxtAllegroMarginUnder5PLN.Text;
                externalConfig.AppSettings.Settings["AllegroMarginBetween5and1000PLNPercent"].Value = TxtAllegroMarginBetween5and1000PLNPercent.Text;
                externalConfig.AppSettings.Settings["AllegroMarginMoreThan1000PLN"].Value = TxtAllegroMarginMoreThan1000PLN.Text;
                externalConfig.AppSettings.Settings["AddPLNToBulkyProducts"].Value = TxtAddPLNToBulkyProducts.Text;
                externalConfig.AppSettings.Settings["AddPLNToCustomProducts"].Value = TxtAddPLNToCustomProducts.Text;
                externalConfig.AppSettings.Settings["AllegroDeliveryName"].Value = TxtAllegroDeliveryName.Text;
                externalConfig.AppSettings.Settings["AllegroHandlingTime"].Value = TxtAllegroHandlingTime.Text;
                externalConfig.AppSettings.Settings["AllegroHandlingTimeCustomProducts"].Value = TxtAllegroHandlingTimeCustomProducts.Text;
                externalConfig.AppSettings.Settings["AllegroSafetyMeasures"].Value = TxtAllegroSafetyMeasures.Text.Replace(Environment.NewLine, "\\r\\n");
                externalConfig.AppSettings.Settings["AllegroWarranty"].Value = TxtAllegroWarranty.Text;
                externalConfig.AppSettings.Settings["AllegroReturnPolicy"].Value = TxtAllegroReturnPolicy.Text;
                externalConfig.AppSettings.Settings["AllegroImpliedWarranty"].Value = TxtAllegroImpliedWarranty.Text;
                externalConfig.AppSettings.Settings["AllegroResponsiblePerson"].Value = TxtAllegroResponsiblePerson.Text;
                externalConfig.AppSettings.Settings["AllegroResponsibleProducer"].Value = TxtAllegroResponsibleProducer.Text;

                externalConfig.Save(ConfigurationSaveMode.Modified);

                ConfigurationManager.RefreshSection("appSettings");
                MessageBox.Show("Konfiguracja zapisana.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ConfigurationErrorsException confEx)
            {
                MessageBox.Show($"Błąd w pliku konfiguracyjnym: {confEx.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się zapisać konfiguracji: {ex.Message}", "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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