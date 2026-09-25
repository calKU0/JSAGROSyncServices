namespace JSAGROSyncServices.Products.Settings
{
    /// <summary>
    /// Dostęp do API Inter Cars. Token OAuth2 (client_credentials) serwis pobiera sam
    /// i odnawia przed wygaśnięciem - w konfiguracji są tylko dane klienta.
    /// </summary>
    public class InterCarsApiCredentials
    {
        public string BaseUrl { get; set; } = "https://api.webapi.intercars.eu";

        public string AuthUrl { get; set; } = "https://is.webapi.intercars.eu/oauth2/token";

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        public string Scope { get; set; } = "allinone";

        /// <summary>Język opisów produktów i nazw kategorii (nagłówek Accept-Language, ISO 639-1).</summary>
        public string Language { get; set; } = "pl";

        /// <summary>Produkty na stronę listy katalogu. API dopuszcza 1-100.</summary>
        public int ProductsPerPage { get; set; } = 100;

        /// <summary>SKU w jednym zapytaniu o stany i ceny. API dopuszcza maksymalnie 100.</summary>
        public int SkuBatchSize { get; set; } = 100;

        /// <summary>
        /// Ile produktów dziennie odpytać o szczegóły. Szczegóły przychodzą po jednym zapytaniu
        /// na SKU, więc pełny katalog rozkłada się na kilka przebiegów. Gdy wszystkie produkty
        /// mają już szczegóły, kolejka wraca do najdawniej odświeżanych.
        /// </summary>
        public int ProductDetailsPerDay { get; set; } = 5000;

        /// <summary>Ile zapytań wykonywać równolegle. API odpowiada 429 przy zbyt dużej równoległości.</summary>
        public int Parallelism { get; set; } = 4;

        /// <summary>Odstęp między kolejnymi zapytaniami jednego wątku (ms).</summary>
        public int RequestDelayMilliseconds { get; set; } = 100;

        /// <summary>
        /// Magazyny, z których sumujemy stan magazynowy. API zwraca wiersz na magazyn;
        /// bierzemy pod uwagę wyłącznie te z listy.
        /// </summary>
        public List<string> Warehouses { get; set; } = new() { "HZA", "BPO" };

        /// <summary>
        /// Do ilu poziomów schodzić przy odświeżaniu globalnego drzewa kategorii. Pełne drzewo
        /// to ponad tysiąc zapytań, więc szeroko schodzimy płytko, a skonfigurowane gałęzie
        /// pobieramy zawsze do końca - to one wyznaczają zakres pobierania produktów.
        /// </summary>
        public int CategoryTreeDepth { get; set; } = 2;
    }
}
