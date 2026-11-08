namespace ServiceManager.Models
{
    public enum TableColumnKind
    {
        Text,
        Int,
        Decimal,

        /// <summary>Wybór z listy <see cref="TableColumn.Choices"/>.</summary>
        Choice,

        /// <summary>Tak / nie - pole wyboru.</summary>
        Bool
    }

    /// <summary>Kolumna pola-tabeli: lista obiektów albo słownik w appsettings.</summary>
    public class TableColumn
    {
        /// <summary>Nazwa właściwości w JSON (dla słownika: pierwsza kolumna to klucz, druga wartość).</summary>
        public string Property { get; set; } = "";

        public string Header { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public TableColumnKind Kind { get; set; } = TableColumnKind.Text;

        /// <summary>Szerokość względna kolumny (jak "2*" w WPF).</summary>
        public double Width { get; set; } = 1;

        public bool Required { get; set; }

        /// <summary>Wartość w nowo dodanym wierszu (np. "true" dla zaznaczonego pola wyboru).</summary>
        public string DefaultValue { get; set; } = "";

        /// <summary>Wartości do wyboru dla <see cref="TableColumnKind.Choice"/>.</summary>
        public List<string> Choices { get; set; } = new();
    }
}
