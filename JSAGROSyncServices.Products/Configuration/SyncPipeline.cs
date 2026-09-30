namespace JSAGROSyncServices.Products.Configuration
{
    /// <summary>
    /// Kroki cyklu synchronizacji wykonywane przez dany serwis.
    /// Dane od dostawcy pobiera tylko jedno konto, więc drugie ma tu wyłączone kroki pobierania.
    /// </summary>
    public sealed class SyncPipeline
    {
        /// <summary>Synchronizacja osób i producentów odpowiedzialnych oraz cenników dostawy z Allegro.</summary>
        public bool SyncAllegroDictionaries { get; init; }

        /// <summary>Pobranie listy produktów od dostawcy (Gąska / Rolmar).</summary>
        public bool FetchSupplierProducts { get; init; }

        /// <summary>Pobranie stanów magazynowych od dostawcy (Rolmar).</summary>
        public bool FetchSupplierStock { get; init; }

        /// <summary>Pobranie zdjęć od dostawcy (Rolmar).</summary>
        public bool FetchSupplierImages { get; init; }

        /// <summary>Pobranie szczegółów produktów od dostawcy - raz dziennie w oknie nocnym (Gąska, Inter Cars).</summary>
        public bool FetchSupplierProductDetailsDaily { get; init; }

        /// <summary>
        /// Pobranie pełnego drzewa kategorii dostawcy - raz dziennie w oknie nocnym (Inter Cars).
        /// Katalog ma kilka tysięcy węzłów, więc w zwykłym cyklu odświeżamy tylko wierzchnie poziomy
        /// i skonfigurowane gałęzie; pełne drzewo jest potrzebne wyłącznie do wyboru kategorii
        /// w Menadżerze Serwisów.
        /// </summary>
        public bool FetchSupplierCategoryTreeDaily { get; init; }

        /// <summary>
        /// Czy wystawiać wyłącznie produkty z pobranymi szczegółami. Dotyczy dostawców,
        /// u których waga i wymiary przychodzą osobnym zapytaniem (Inter Cars) - bez nich
        /// cennik dostawy wybrałby się na podstawie zerowej wagi.
        /// </summary>
        public bool RequireProductDetails { get; init; }

        /// <summary>Pobranie szczegółów ofert z Allegro (opisy, parametry).</summary>
        public bool SyncOfferDetails { get; init; }

        /// <summary>Aktualizacja drzewa kategorii Allegro (raz dziennie razem ze szczegółami produktów).</summary>
        public bool UpdateAllegroCategoriesDaily { get; init; }

        /// <summary>Aktualizacja drzewa kategorii Allegro w każdym cyklu.</summary>
        public bool UpdateAllegroCategories { get; init; }

        /// <summary>Wyszukiwanie produktów Allegro dla pozycji bez domyślnej kategorii.</summary>
        public bool SearchAllegroProducts { get; init; }

        /// <summary>Pobranie parametrów kategorii i uzupełnienie parametrów produktów.</summary>
        public bool SyncProductParameters { get; init; }

        /// <summary>Godzina, od której może ruszyć krok dzienny.</summary>
        public int DailyWindowStartHour { get; init; } = 2;

        /// <summary>Godzina, do której może ruszyć krok dzienny.</summary>
        public int DailyWindowEndHour { get; init; } = 8;
    }
}
