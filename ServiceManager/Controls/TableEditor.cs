using Microsoft.Extensions.Configuration;
using ServiceManager.Models;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ServiceManager.Controls
{
    /// <summary>
    /// Edytor pola-tabeli: lista obiektów (np. cenniki dostaw, progi marży) albo słownik
    /// (np. mapa kategorii). Kolumny, sortowanie i reguły całej tabeli opisuje definicja pola,
    /// więc kolejne takie pola nie wymagają nowego edytora.
    /// </summary>
    public class TableEditor : UserControl
    {
        private readonly ConfigField _field;
        private readonly List<TableColumn> _columns;
        private readonly StackPanel _rowsPanel = new();
        private readonly List<List<Control>> _rows = new();

        public TableEditor(ConfigField field)
        {
            _field = field;
            _columns = field.Columns.ToList();

            var root = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            var header = NewRowGrid();

            for (var i = 0; i < _columns.Count; i++)
            {
                var text = new TextBlock
                {
                    Text = _columns[i].Header,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(2),
                    TextWrapping = TextWrapping.Wrap,
                    ToolTip = string.IsNullOrEmpty(_columns[i].Tooltip) ? null : _columns[i].Tooltip
                };

                Grid.SetColumn(text, i);
                header.Children.Add(text);
            }

            var add = new Button { Content = field.AddButtonText, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
            add.Click += (_, _) => AddRow(_columns.ToDictionary(c => c.Property, c => c.DefaultValue));

            root.Children.Add(header);
            root.Children.Add(_rowsPanel);
            root.Children.Add(add);

            Content = root;
        }

        /// <summary>Wczytuje wiersze z sekcji konfiguracji (tablica obiektów albo obiekt-słownik).</summary>
        public void Load(IConfigurationSection section)
        {
            _rowsPanel.Children.Clear();
            _rows.Clear();

            foreach (var child in section.GetChildren())
            {
                var values = new Dictionary<string, string>();

                if (_field.IsDictionary)
                {
                    values[_columns[0].Property] = child.Key;
                    values[_columns[1].Property] = child.Value ?? string.Empty;
                }
                else
                {
                    // Brak właściwości w pliku = wartość domyślna kolumny (np. "Smart" domyślnie zaznaczony,
                    // tak jak w klasie ustawień serwisu).
                    foreach (var column in _columns)
                        values[column.Property] = child[column.Property] ?? column.DefaultValue;
                }

                AddRow(values);
            }
        }

        /// <summary>Wiersze jako JSON gotowy do zapisu; błędy walidacji trafiają do <paramref name="errors"/>.</summary>
        public string ToJson(List<string> errors)
        {
            var array = new JsonArray();
            var dictionary = new JsonObject();
            var label = _field.Label;
            var rowErrors = new List<string>();

            for (var r = 0; r < _rows.Count; r++)
            {
                var texts = _rows[r].Select(ReadValue).ToList();

                // Wiersz bez żadnej wpisanej wartości (pola wyboru się nie liczą) to zwykle niedokończone "Dodaj".
                if (_columns.Select((c, i) => c.Kind == TableColumnKind.Bool || string.IsNullOrWhiteSpace(texts[i])).All(empty => empty))
                    continue;

                var item = new JsonObject();

                for (var c = 0; c < _columns.Count; c++)
                {
                    var column = _columns[c];
                    var text = texts[c];

                    if (column.Required && string.IsNullOrWhiteSpace(text))
                    {
                        rowErrors.Add($"„{label}”: kolumna „{column.Header}” jest wymagana.");
                        continue;
                    }

                    item[column.Property] = ToNode(column, text, rowErrors);
                }

                if (_field.IsDictionary)
                {
                    var key = texts[0];

                    if (dictionary.ContainsKey(key))
                        rowErrors.Add($"„{label}”: wartość „{key}” występuje więcej niż raz.");
                    else
                        dictionary[key] = item[_columns[1].Property]?.DeepClone();
                }
                else
                {
                    array.Add(item);
                }
            }

            if (!_field.IsDictionary && !string.IsNullOrEmpty(_field.SortBy))
                array = Sorted(array, _field.SortBy);

            // Reguły całej tabeli mają sens dopiero, gdy pojedyncze wartości są poprawne.
            if (rowErrors.Count == 0 && !_field.IsDictionary && _field.TableValidator != null)
                rowErrors.AddRange(_field.TableValidator(array));

            errors.AddRange(rowErrors.Distinct());

            return _field.IsDictionary ? dictionary.ToJsonString() : array.ToJsonString();
        }

        private static JsonArray Sorted(JsonArray array, string property)
        {
            var ordered = array
                .Select(n => n!.DeepClone())
                .OrderBy(n => n[property] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : decimal.MaxValue)
                .ToArray();

            return new JsonArray(ordered);
        }

        private JsonNode? ToNode(TableColumn column, string text, List<string> errors)
        {
            if (column.Kind == TableColumnKind.Bool)
                return JsonValue.Create(string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(text))
                return column.Kind is TableColumnKind.Int or TableColumnKind.Decimal ? JsonValue.Create(0) : JsonValue.Create(string.Empty);

            switch (column.Kind)
            {
                case TableColumnKind.Int:
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                        return JsonValue.Create(integer);
                    break;

                case TableColumnKind.Decimal:
                    if (decimal.TryParse(text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                        return JsonValue.Create(number);
                    break;

                default:
                    return JsonValue.Create(text);
            }

            errors.Add($"„{_field.Label}”: „{column.Header}” wymaga liczby, a jest „{text}”.");
            return JsonValue.Create(text);
        }

        private void AddRow(IReadOnlyDictionary<string, string> values)
        {
            var grid = NewRowGrid();
            var controls = new List<Control>();

            for (var i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];
                values.TryGetValue(column.Property, out var value);

                Control control = column.Kind switch
                {
                    TableColumnKind.Choice => new ComboBox
                    {
                        ItemsSource = column.Choices,
                        SelectedItem = column.Choices.FirstOrDefault(c => string.Equals(c, value, StringComparison.OrdinalIgnoreCase)) ?? column.Choices.FirstOrDefault(),
                        Margin = new Thickness(2)
                    },
                    TableColumnKind.Bool => new CheckBox
                    {
                        IsChecked = bool.TryParse(value, out var flag) && flag,
                        Margin = new Thickness(6, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        ToolTip = string.IsNullOrEmpty(column.Tooltip) ? null : column.Tooltip
                    },
                    _ => new TextBox
                    {
                        Text = FormatNumber(column, value),
                        Margin = new Thickness(2),
                        TextWrapping = TextWrapping.Wrap
                    }
                };

                Grid.SetColumn(control, i);
                grid.Children.Add(control);
                controls.Add(control);
            }

            var remove = new Button
            {
                Content = "✖",
                Foreground = Brushes.Red,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0)
            };

            remove.Click += (_, _) =>
            {
                _rowsPanel.Children.Remove(grid);
                _rows.Remove(controls);
            };

            Grid.SetColumn(remove, _columns.Count);
            grid.Children.Add(remove);

            _rowsPanel.Children.Add(grid);
            _rows.Add(controls);
        }

        /// <summary>Liczby pokazujemy z kropką, niezależnie od ustawień regionalnych systemu.</summary>
        private static string FormatNumber(TableColumn column, string? value)
        {
            if (column.Kind == TableColumnKind.Decimal
                && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                return number.ToString(CultureInfo.InvariantCulture);

            return value ?? string.Empty;
        }

        private Grid NewRowGrid()
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };

            foreach (var column in _columns)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width, GridUnitType.Star) });

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            return grid;
        }

        private static string ReadValue(Control control) => control switch
        {
            TextBox textBox => textBox.Text.Trim(),
            ComboBox comboBox => comboBox.SelectedItem as string ?? string.Empty,
            CheckBox checkBox => checkBox.IsChecked == true ? "true" : "false",
            _ => string.Empty
        };
    }
}
