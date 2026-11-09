using ServiceManager.Enums;

namespace ServiceManager.Models
{
    public class ConfigField
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "Other";
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Czy pole pokazywać w konfiguratorze. <c>false</c> ukrywa je całkowicie -
        /// wartość zostaje w appsettings nietknięta, bo zapis nadpisuje tylko pola z ekranu.
        /// </summary>
        public bool IsVisable { get; set; } = true;
        public ConfigFieldType FieldType { get; set; } = ConfigFieldType.String;

        /// <summary>
        /// Dla list kategorii: dostawca (1 = Rolmar, 2 = Gąska, 3 = Inter Cars), którego kategorie podpowiadać z bazy.
        /// <c>null</c> - zwykła lista wpisywana ręcznie.
        /// </summary>
        public int? CategoryCompany { get; set; }

        /// <summary>Pole tekstowe wielowierszowe (długie teksty, np. polityka zwrotów).</summary>
        public bool Multiline { get; set; }

        /// <summary>Kolumny pola typu <see cref="ConfigFieldType.Table"/>.</summary>
        public List<TableColumn> Columns { get; set; } = new();

        /// <summary>
        /// Tabela zapisywana jako obiekt-słownik (pierwsza kolumna = klucz, druga = wartość)
        /// zamiast tablicy obiektów.
        /// </summary>
        public bool IsDictionary { get; set; }

        /// <summary>Tekst przycisku dodającego wiersz tabeli.</summary>
        public string AddButtonText { get; set; } = "Dodaj";

        /// <summary>
        /// Właściwość liczbowa, po której tabela jest sortowana rosnąco przy zapisie
        /// (np. progi marży po "Min" - serwisy traktują ostatni próg jako zapasowy).
        /// </summary>
        public string? SortBy { get; set; }

        /// <summary>
        /// Reguły dotyczące całej tabeli (np. progi marży się nie nakładają). Dostaje wiersze
        /// już po przeliczeniu typów i zwraca komunikaty błędów.
        /// </summary>
        public Func<System.Text.Json.Nodes.JsonArray, IEnumerable<string>>? TableValidator { get; set; }
    }
}