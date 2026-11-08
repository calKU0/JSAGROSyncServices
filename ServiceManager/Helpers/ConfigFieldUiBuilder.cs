using Microsoft.Extensions.Configuration;
using ServiceManager.Controls;
using ServiceManager.Enums;
using ServiceManager.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Windows;
using System.Windows.Controls;

namespace ServiceManager.Helpers
{
    public class ConfigFieldUiBuilder
    {
        public void AddFields(StackPanel panel, IEnumerable<ConfigField> fields, IConfiguration config)
        {
            foreach (var field in fields)
            {
                if (field.CategoryCompany != null)
                {
                    // Lista kategorii dostawcy z bazy tej usługi - connection string bierzemy z jej konfiguracji.
                    panel.Children.Add(CreateCategoryPickerRow(field, ReadListValues(config, field.Key), config.GetConnectionString("MyDbContext")));
                    continue;
                }

                if (field.FieldType == ConfigFieldType.Table)
                {
                    panel.Children.Add(CreateTableRow(field, config.GetSection(field.Key)));
                    continue;
                }

                if (field.FieldType == ConfigFieldType.Bool)
                {
                    panel.Children.Add(CreateBoolRow(field, config[field.Key]));
                    continue;
                }

                if (IsListField(field))
                {
                    panel.Children.Add(CreateListRow(field, ReadListValues(config, field.Key)));
                    continue;
                }

                var value = config[field.Key] ?? string.Empty;
                panel.Children.Add(CreateRow(field, value));
            }
        }

        public Grid CreateRow(ConfigField field, string value)
        {
            var multiline = field.Multiline || field.Key.EndsWith("AllegroSafetyMeasures", StringComparison.Ordinal);

            var textbox = new TextBox
            {
                Text = value,
                Margin = new Thickness(0, 4, 0, 4),
                IsEnabled = field.IsEnabled,
                Tag = field.Key,
                AcceptsReturn = multiline,
                Height = multiline ? 120 : Double.NaN,
                VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
                TextWrapping = TextWrapping.Wrap
            };

            return CreateRow(field, textbox);
        }

        /// <summary>
        /// Obsługuje też stary format konfiguracji, w którym lista była pojedynczym ciągiem
        /// rozdzielonym przecinkami - inaczej zapis nadpisałby ją pustą tablicą.
        /// </summary>
        private static List<string> ReadListValues(IConfiguration config, string key)
        {
            var section = config.GetSection(key);

            if (!string.IsNullOrWhiteSpace(section.Value))
            {
                return section.Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            return section.Get<List<string>>() ?? new List<string>();
        }

        public Grid CreateBoolRow(ConfigField field, string? value)
        {
            var checkBox = new CheckBox
            {
                IsChecked = bool.TryParse(value, out var flag) && flag,
                Margin = new Thickness(0, 6, 0, 6),
                IsEnabled = field.IsEnabled,
                Tag = field.Key,
                VerticalAlignment = VerticalAlignment.Center
            };

            return CreateRow(field, checkBox);
        }

        /// <summary>Tabela zajmuje całą szerokość - etykieta nad nią, a nie obok.</summary>
        public Grid CreateTableRow(ConfigField field, IConfigurationSection section)
        {
            var editor = new TableEditor(field)
            {
                IsEnabled = field.IsEnabled,
                Tag = field.Key
            };

            editor.Load(section);

            var label = new TextBlock
            {
                Text = field.Label,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var description = new TextBlock
            {
                Text = field.Description,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.Gray,
                Visibility = string.IsNullOrEmpty(field.Description) ? Visibility.Collapsed : Visibility.Visible
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(label, 0);
            Grid.SetRow(description, 1);
            Grid.SetRow(editor, 2);

            grid.Children.Add(label);
            grid.Children.Add(description);
            grid.Children.Add(editor);

            return grid;
        }

        public Grid CreateCategoryPickerRow(ConfigField field, IEnumerable<string> selectedKeys, string? connectionString)
        {
            var picker = new CategoryPickerEditor
            {
                Margin = new Thickness(0, 4, 0, 4),
                IsEnabled = field.IsEnabled,
                Tag = field.Key
            };

            picker.Initialize(connectionString, field.CategoryCompany!.Value, selectedKeys);

            return CreateRow(field, picker);
        }

        public Grid CreateListRow(ConfigField field, IEnumerable<string> items)
        {
            var editor = new StringListEditor
            {
                Margin = new Thickness(0, 4, 0, 4),
                IsEnabled = field.IsEnabled,
                Tag = field.Key
            };

            editor.SetItems(items);

            return CreateRow(field, editor);
        }

        private static Grid CreateRow(ConfigField field, UIElement editor)
        {
            var label = new TextBlock
            {
                Text = field.Label,
                Margin = new Thickness(0, 4, 0, 4),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = string.IsNullOrEmpty(field.Description) ? null : field.Description
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(295) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(label, 0);
            Grid.SetColumn(editor, 1);

            grid.Children.Add(label);
            grid.Children.Add(editor);

            return grid;
        }

        private static bool IsListField(ConfigField field)
        {
            return field.FieldType == ConfigFieldType.StringList || field.FieldType == ConfigFieldType.IntList;
        }
    }
}
