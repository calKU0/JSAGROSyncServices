using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ServiceManager.Controls
{
    public partial class StringListEditor : UserControl, IListValueEditor
    {
        private readonly List<TextBox> _rows = new();

        public StringListEditor()
        {
            InitializeComponent();
        }

        public void SetItems(IEnumerable<string> items)
        {
            RowsPanel.Children.Clear();
            _rows.Clear();

            foreach (var item in items)
            {
                AddRow(item);
            }
        }

        public IReadOnlyList<string> GetItems()
        {
            return _rows
                .Select(r => r.Text.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddRow(string.Empty);
        }

        private void AddRow(string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBox = new TextBox { Text = value, Margin = new Thickness(0, 0, 4, 0) };

            var removeBtn = new Button
            {
                Content = "✖",
                Foreground = Brushes.Red,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0)
            };

            removeBtn.Click += (_, _) =>
            {
                RowsPanel.Children.Remove(grid);
                _rows.Remove(textBox);
            };

            Grid.SetColumn(textBox, 0);
            Grid.SetColumn(removeBtn, 1);

            grid.Children.Add(textBox);
            grid.Children.Add(removeBtn);

            RowsPanel.Children.Add(grid);
            _rows.Add(textBox);
        }
    }
}
