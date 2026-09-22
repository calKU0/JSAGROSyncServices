using ServiceManager.Controls;
using ServiceManager.Enums;
using ServiceManager.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ServiceManager.Helpers
{
    public class ConfigFieldValueCollector
    {
        public (Dictionary<string, string> Values, List<string> Errors) CollectValues(Panel rootPanel, IEnumerable<ConfigField> fieldDefinitions)
        {
            var values = new Dictionary<string, string>();
            var errors = new List<string>();
            var definitionsByKey = fieldDefinitions.ToDictionary(f => f.Key, f => f);

            foreach (var groupBox in rootPanel.Children.OfType<GroupBox>())
            {
                if (groupBox.Content is not StackPanel stackPanel)
                    continue;

                foreach (var grid in stackPanel.Children.OfType<Grid>())
                {
                    // Lista wpisywana ręcznie i lista kategorii zapisują się tak samo.
                    var listEditor = grid.Children.OfType<IListValueEditor>().FirstOrDefault();
                    if (listEditor is FrameworkElement { Tag: string listKey })
                    {
                        definitionsByKey.TryGetValue(listKey, out var listFieldDef);
                        values[listKey] = SerializeList(listEditor.GetItems(), listFieldDef, errors);
                        continue;
                    }

                    var tb = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (tb == null || tb.Tag is not string key)
                        continue;

                    var value = tb.Text.Trim();

                    if (definitionsByKey.TryGetValue(key, out var fieldDef))
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

                    values[key] = value;
                }
            }

            return (values, errors);
        }

        private static string SerializeList(IReadOnlyList<string> items, ConfigField? fieldDef, List<string> errors)
        {
            if (fieldDef == null || fieldDef.FieldType != ConfigFieldType.IntList)
                return JsonSerializer.Serialize(items);

            var numbers = new List<int>();

            foreach (var item in items)
            {
                if (int.TryParse(item, out var number))
                    numbers.Add(number);
                else
                    errors.Add($"Pole „{fieldDef.Label}” wymaga liczb całkowitych — „{item}” nie jest liczbą.");
            }

            return JsonSerializer.Serialize(numbers);
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
