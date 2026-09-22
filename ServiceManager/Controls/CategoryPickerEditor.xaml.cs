using ServiceManager.Models;
using ServiceManager.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ServiceManager.Controls
{
    /// <summary>
    /// Wybór kategorii dostawcy z listy dostępnych, z wyszukiwaniem po nazwie.
    /// Do konfiguracji trafia klucz kategorii (Gąska: id, Rolmar: ścieżka), użytkownik widzi pełną ścieżkę.
    /// Gdy lista jest niedostępna (np. brak połączenia z bazą), wartość można wpisać ręcznie.
    /// </summary>
    public partial class CategoryPickerEditor : UserControl, IListValueEditor
    {
        // Tysiące pozycji w liście rozwijanej tylko spowalniają - dalsze zawężanie robi użytkownik.
        private const int MaxSuggestions = 200;

        private readonly List<string> _selectedKeys = new();
        private IReadOnlyList<CategoryOption> _options = Array.Empty<CategoryOption>();
        private Dictionary<string, CategoryOption> _optionsByKey = new(StringComparer.OrdinalIgnoreCase);
        private bool _catalogAvailable;

        public CategoryPickerEditor()
        {
            InitializeComponent();
        }

        /// <summary>Ustawia wybrane kategorie i ładuje listę dostępnych w tle.</summary>
        public async void Initialize(string? connectionString, int integrationCompany, IEnumerable<string> selectedKeys)
        {
            _selectedKeys.Clear();
            _selectedKeys.AddRange(selectedKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
            RenderSelected();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                ShowStatus("Brak connection stringa w konfiguracji usługi - wpisz wartość ręcznie i naciśnij Enter.", isError: true);
                return;
            }

            ShowStatus("Ładowanie listy kategorii…", isError: false);

            try
            {
                _options = await new SupplierCategoryCatalog().LoadAsync(connectionString, integrationCompany);
                _optionsByKey = _options.ToDictionary(o => o.Key, StringComparer.OrdinalIgnoreCase);
                _catalogAvailable = true;

                if (_options.Count == 0)
                {
                    ShowStatus("Lista kategorii jest pusta - usługa uzupełni ją przy najbliższym pobraniu produktów.", isError: true);
                    _catalogAvailable = false;
                }
                else
                {
                    HideStatus();
                }

                RenderSelected();
            }
            catch (Exception ex)
            {
                // async void - wyjątek nie może stąd wypłynąć, bo zamknąłby aplikację.
                ShowStatus($"Nie udało się pobrać listy kategorii ({ex.Message}). Możesz wpisać wartość ręcznie i nacisnąć Enter.", isError: true);
            }
        }

        public IReadOnlyList<string> GetItems() => _selectedKeys.ToList();

        // ------------------------------------------------------------------ wyszukiwanie

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshSuggestions();
        }

        private void SearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            SearchHint.Visibility = Visibility.Collapsed;
            RefreshSuggestions();
        }

        private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(SearchBox.Text))
                SearchHint.Visibility = Visibility.Visible;
        }

        private void RefreshSuggestions()
        {
            if (!_catalogAvailable)
            {
                SuggestionsPopup.IsOpen = false;
                return;
            }

            var terms = TolerantSearch.Terms(SearchBox.Text);

            var matches = _options
                .Where(o => !_selectedKeys.Contains(o.Key, StringComparer.OrdinalIgnoreCase))
                .Where(o => terms.Length == 0 || TolerantSearch.Matches(o.SearchText, terms))
                .ToList();

            SuggestionsList.ItemsSource = matches.Take(MaxSuggestions).ToList();
            SuggestionsInfo.Text = matches.Count > MaxSuggestions
                ? $"Pokazano {MaxSuggestions} z {matches.Count} - wpisz więcej, aby zawęzić."
                : matches.Count == 0 ? "Brak pasujących kategorii." : $"{matches.Count} kategorii.";

            SuggestionsPopup.IsOpen = SearchBox.IsKeyboardFocusWithin;
        }

        private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down when SuggestionsPopup.IsOpen && SuggestionsList.Items.Count > 0:
                    SuggestionsList.SelectedIndex = 0;
                    ((ListBoxItem?)SuggestionsList.ItemContainerGenerator.ContainerFromIndex(0))?.Focus();
                    e.Handled = true;
                    break;

                case Key.Enter:
                    AddFromSearch();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    SuggestionsPopup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private void SuggestionsList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SuggestionsList.SelectedItem is CategoryOption option)
            {
                Add(option.Key);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                SuggestionsPopup.IsOpen = false;
                SearchBox.Focus();
                e.Handled = true;
            }
        }

        private void SuggestionsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemsControl.ContainerFromElement(SuggestionsList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: CategoryOption option })
                Add(option.Key);
        }

        /// <summary>Enter w polu wyszukiwania: pierwsza podpowiedź albo - bez listy - wpisana wartość.</summary>
        private void AddFromSearch()
        {
            if (_catalogAvailable)
            {
                if (SuggestionsList.Items.Count > 0 && SuggestionsList.Items[0] is CategoryOption first)
                    Add(first.Key);

                return;
            }

            var manual = SearchBox.Text.Trim();

            if (manual.Length > 0)
                Add(manual);
        }

        private void Add(string key)
        {
            if (!_selectedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                _selectedKeys.Add(key);
                RenderSelected();
            }

            SearchBox.Clear();
            SearchBox.Focus();
            RefreshSuggestions();
        }

        // ------------------------------------------------------------------ wybrane

        private void RenderSelected()
        {
            SelectedPanel.Children.Clear();

            if (_selectedKeys.Count == 0)
            {
                SelectedPanel.Children.Add(new TextBlock
                {
                    Text = "Brak wybranych kategorii - oferty nie będą filtrowane po kategorii.",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 2)
                });
                return;
            }

            foreach (var key in _selectedKeys.OrderBy(Describe, StringComparer.CurrentCultureIgnoreCase))
                SelectedPanel.Children.Add(CreateSelectedRow(key));
        }

        private UIElement CreateSelectedRow(string key)
        {
            var known = _optionsByKey.TryGetValue(key, out var option);

            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = Describe(key),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = known ? $"{option!.Path}\nKlucz: {key}" : $"Klucz: {key}",
                // Kategoria spoza listy: literówka albo kategoria usunięta u dostawcy.
                Foreground = known || !_catalogAvailable ? Brushes.Black : Brushes.DarkOrange
            };

            var remove = new Button
            {
                Content = "✖",
                Foreground = Brushes.Red,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(6, 0, 6, 0),
                ToolTip = "Usuń kategorię"
            };

            remove.Click += (_, _) =>
            {
                _selectedKeys.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                RenderSelected();
                RefreshSuggestions();
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(remove, 1);
            grid.Children.Add(label);
            grid.Children.Add(remove);

            return grid;
        }

        private string Describe(string key)
        {
            if (_optionsByKey.TryGetValue(key, out var option))
                return option.Path;

            return _catalogAvailable ? $"{key}  (brak na liście kategorii)" : key;
        }

        private void ShowStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? Brushes.DarkOrange : Brushes.Gray;
            StatusText.Visibility = Visibility.Visible;
        }

        private void HideStatus() => StatusText.Visibility = Visibility.Collapsed;
    }
}
