using JSAGROAllegroServiceConfiguration.Enums;
using JSAGROAllegroServiceConfiguration.Models;
using System;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace JSAGROServiceManager
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<LogFileItem> logFiles = new ObservableCollection<LogFileItem>();
        private ObservableCollection<LogLine> currentLogLines = new ObservableCollection<LogLine>();
        private DispatcherTimer refreshTimer;
        private ServiceController _serviceController;

        private readonly string _externalConfigPath = ConfigurationManager.AppSettings["ExternalConfigPath"].ToString();
        private readonly string _logFolderPath = ConfigurationManager.AppSettings["LogFolderPath"].ToString();
        private readonly string _serviceName = ConfigurationManager.AppSettings["ServiceName"].ToString();

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serviceController = new ServiceController(_serviceName);
            RefreshServiceStatus();
        }

        private void BtnShowLogs_Click(object sender, RoutedEventArgs e)
        {
            MainContentArea.Visibility = Visibility.Visible;
            LogsViewContainer.Visibility = Visibility.Visible;
            ConfigViewContainer.Visibility = Visibility.Collapsed;

            LvLogFiles.ItemsSource = logFiles;
            IcLogLines.ItemsSource = currentLogLines;

            // Auto-refresh timer setup
            refreshTimer = new DispatcherTimer();
            refreshTimer.Interval = TimeSpan.FromSeconds(5);
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

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshServiceStatus();
            if (LogsViewContainer.Visibility == Visibility.Visible && LvLogFiles.SelectedItem != null)
            {
                var scrollOffset = SvLogContent.VerticalOffset;
                var maxOffset = SvLogContent.ScrollableHeight;
                bool isAtBottom = Math.Abs(maxOffset - scrollOffset) < 1;

                LoadSelectedFileContent();

                if (isAtBottom)
                {
                    SvLogContent.ScrollToEnd();
                }
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
                foreach (var filePath in Directory.GetFiles(_logFolderPath, "*.txt"))
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
                                if (line.Contains("WRN", StringComparison.Ordinal)) warnings++;
                                if (line.Contains("ERR", StringComparison.Ordinal)) errors++;
                            }
                        }

                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        string datePart = fileName.Replace("log-", "");

                        string formattedDate = fileName;
                        if (DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                        {
                            formattedDate = parsedDate.ToString("dd.MM.yyyy");
                        }

                        logFiles.Add(new LogFileItem
                        {
                            Name = formattedDate,
                            Path = filePath,
                            WarningsCount = warnings,
                            ErrorsCount = errors
                        });
                    }
                    catch (IOException ioEx)
                    {
                        MessageBox.Show($"Błąd odczytu pliku logu {filePath}: {ioEx.Message}");
                    }
                    catch (UnauthorizedAccessException unauthEx)
                    {
                        MessageBox.Show($"Brak dostępu do pliku logu {filePath}: {unauthEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Nie udało się załadować listy plików logów: {ex.Message}");
            }
        }

        private void LoadSelectedFileContent()
        {
            currentLogLines.Clear();

            if (LvLogFiles.SelectedItem is LogFileItem item && File.Exists(item.Path))
            {
                try
                {
                    using (var fs = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            LogLevel level = LogLevel.Information;
                            if (line.Contains("ERR", StringComparison.Ordinal)) level = LogLevel.Error;
                            else if (line.Contains("WRN", StringComparison.Ordinal)) level = LogLevel.Warning;

                            currentLogLines.Add(new LogLine { Level = level, Message = line });
                        }
                    }
                }
                catch (IOException ioEx)
                {
                    MessageBox.Show($"Błąd odczytu logu {item.Name}: {ioEx.Message}");
                }
                catch (UnauthorizedAccessException unauthEx)
                {
                    MessageBox.Show($"Brak dostępu do logu {item.Name}: {unauthEx.Message}");
                }
            }
        }

        private void LvLogFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LvLogFiles.SelectedItem != null)
            {
                TxtSelectedFileName.Text = ((LogFileItem)LvLogFiles.SelectedItem).Name;
                LoadSelectedFileContent();
            }
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