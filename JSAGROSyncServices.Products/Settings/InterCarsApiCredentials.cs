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
        /// Górny limit produktów odpytywanych o szczegóły w jednym przebiegu. Inter Cars nie limituje
        /// liczby zapytań, więc domyślne 0 oznacza "wszystkie, które tego wymagają" - hamulcem jest
        /// <see cref="Parallelism"/> i <see cref="RequestDelayMilliseconds"/>. Wartość dodatnia
        /// przycina przebieg, np. przy diagnozie.
        /// </summary>
        public int ProductDetailsPerRun { get; set; }

        /// <summary>Po ilu dniach odświeżać szczegóły produktu, który już je ma.</summary>
        public int ProductDetailsRefreshDays { get; set; } = 7;

        /// <summary>Ile zapytań wykonywać równolegle. API odpowiada 429 przy zbyt dużej równoległości.</summary>
        public int Parallelism { get; set; } = 4;

        /// <summary>Odstęp między kolejnymi zapytaniami jednego wątku (ms).</summary>
        public int RequestDelayMilliseconds { get; set; } = 100;

        /// <summary>
        /// Magazyny, z których sumujemy stan magazynowy. API zwraca wiersz na magazyn;
        /// bierzemy pod uwagę wyłącznie te z listy.
        /// </summary>
        public List<string> Warehouses { get; set; } = new() { "HZA", "BPO" };

        // ------------------------------------------------------------ wymiana plików CSV

        /// <summary>
        /// Adres serwera z plikami CSV. API nie umie zawęzić katalogu do asortymentu rolniczego,
        /// a te pliki są już przefiltrowane do produktów AGRO - synchronizujemy wyłącznie SKU,
        /// które się w nich znajdują.
        /// </summary>
        public string DataBaseUrl { get; set; } = "https://data.webapi.intercars.eu/customer/";

        /// <summary>Numer klienta - jednocześnie nazwa katalogu z plikami na serwerze.</summary>
        public string DataCustomerNumber { get; set; } = string.Empty;

        /// <summary>Użytkownik do wymiany plików CSV (Basic Auth). Inny niż dane klienta API.</summary>
        public string DataUser { get; set; } = string.Empty;

        public string DataPassword { get; set; } = string.Empty;

        /// <summary>
        /// Osobne konto na serwerze wymiany dla katalogu <c>Pictures</c>. Konto rolnicze ma tylko
        /// listę asortymentu AGRO - zdjęcia wystawia konto z pełnym katalogiem. Puste pole oznacza,
        /// że zdjęcia bierzemy z tego samego konta co resztę plików.
        /// </summary>
        public string PicturesCustomerNumber { get; set; } = string.Empty;

        public string PicturesUser { get; set; } = string.Empty;

        public string PicturesPassword { get; set; } = string.Empty;
    }
}
