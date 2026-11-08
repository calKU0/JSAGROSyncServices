using Microsoft.Extensions.Configuration;
using ServiceManager.Enums;
using ServiceManager.Helpers;
using ServiceManager.Models;
using ServiceManager.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ServiceManager.Validation;
using ServiceManager.Resources;

namespace ServiceManager
{
    public partial class MainWindow
    {
        private void ShowConfigViewInternal()
        {
            LoadConfig();
            ShowConfigView();
        }

        private IConfigurationRoot LoadAppSettings(string path)
        {
            return _configService.LoadAppSettings(path);
        }

        private void SaveAppSettings(string path, Dictionary<string, string> values)
        {
            // Typy pól decydują o tym, czy wartość trafi do pliku jako liczba, czy jako tekst.
            var fieldTypes = ConfigFieldDefinitions.AllFields
                .GroupBy(f => f.Key)
                .ToDictionary(g => g.Key, g => g.First().FieldType);

            _configService.SaveAppSettings(path, values, fieldTypes);
        }

        private void LoadConfig()
        {
            if (_selectedService == null) return;

            try
            {
                var config = LoadAppSettings(_selectedService.ExternalConfigPath);

                ConfigStackPanel.Children.Clear();

                var groupedFields = ConfigFieldDefinitions.AllFields
                    .Where(f => f.IsVisable)
                    .GroupBy(f => f.Group);

                foreach (var group in groupedFields)
                {
                    var existingFields = group
                        .Where(f => FieldExists(config, f))
                        .ToList();

                    if (!existingFields.Any())
                        continue;

                    var groupBox = new GroupBox { Header = group.Key, Margin = new Thickness(0, 6, 0, 6) };
                    var groupPanel = new StackPanel { Margin = new Thickness(6) };

                    _configFieldUiBuilder.AddFields(groupPanel, existingFields, config);

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
                _dialogService.ShowError(string.Format(UiMessages.ConfigLoadFailed, ex.Message));
            }
        }

        private bool FieldExists(IConfiguration config, ConfigField field)
        {
            if (field.FieldType is ConfigFieldType.StringList or ConfigFieldType.IntList or ConfigFieldType.Table)
            {
                // Pusta tablica jest poprawną konfiguracją, a IConfiguration jej nie widzi -
                // dlatego zaglądamy wprost do pliku.
                var path = _selectedService?.ExternalConfigPath;

                return !string.IsNullOrWhiteSpace(path) && _configService.KeyExists(path, field.Key);
            }

            return config.GetSection(field.Key).Exists();
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
                var errors = new List<string>();

                var (valuesToSave, fieldErrors) = _configFieldValueCollector
                    .CollectValues(ConfigStackPanel, ConfigFieldDefinitions.AllFields);
                errors.AddRange(fieldErrors);

                if (!TryShowValidationErrors(errors))
                    return;

                SaveAppSettings(_selectedService.ExternalConfigPath, valuesToSave);

                _dialogService.ShowInfo(UiMessages.ConfigSaved);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(string.Format(UiMessages.ConfigSaveFailed, ex.Message));
            }
        }


        private bool TryShowValidationErrors(List<string> errors)
        {
            if (!errors.Any())
                return true;

            _dialogService.ShowWarning(string.Join("\n", errors));
            return false;
        }

    }
}
