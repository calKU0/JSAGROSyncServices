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
            var multiline = field.Key.EndsWith("AllegroSafetyMeasures", StringComparison.Ordinal);

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
