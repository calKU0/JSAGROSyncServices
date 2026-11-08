using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Products.Settings;

namespace Allegro.JSAGRO.InPost.ProductsService.Settings
{
    /// <summary>
    /// Dostęp do API InPost Von Halsky. Jesteśmy sprzedawcą integrującym własny sklep,
    /// więc wystarcza Client Credentials - bez logowania użytkownika w przeglądarce.
    /// </summary>
    public class InPostApiCredentials
    {
        public string BaseUrl { get; set; } = "https://api.inpost-group.com/inpsa/";
        public string TokenUrl { get; set; } = "https://account.inpost-group.com/oauth2/token";

        /// <summary>Nazwa aplikacji w Merchant Portalu - tylko informacyjnie.</summary>
        public string ClientName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string Scope { get; set; } = "openid api:categories:read api:offers:read api:offers:write";

        /// <summary>Id sklepu z Merchant Portalu. Endpoint organizacji jest tylko dla integratorów.</summary>
        public string OrganizationId { get; set; } = string.Empty;
    }

    public class AppSettings
    {
        public int LogsExpirationDays { get; set; } = 14;
        public int FetchIntervalMinutes { get; set; } = 120;

        /// <summary>
        /// Tryb próbny: serwis buduje oferty i loguje, ile jest gotowych i dlaczego reszta odpadła,
        /// ale nic nie wysyła do InPost (żadnych POST/PATCH). Odczyt kategorii i atrybutów działa normalnie.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Bezpiecznik: jeśli w jednym cyklu do zamknięcia wychodzi więcej niż tyle procent ofert
        /// w InPost, nic nie jest zamykane, a w logu pojawia się błąd. Chroni przed zamknięciem
        /// całego asortymentu przez chwilowo złe dane (np. stan 0 w trakcie pobierania produktów
        /// od dostawcy, pusta mapa kategorii). 0 = bez bezpiecznika.
        /// </summary>
        public decimal MaxClosePercentPerCycle { get; set; } = 10m;

        /// <summary>Jeden cykl i koniec - do ręcznych uruchomień i testów.</summary>
        public bool RunOnce { get; set; }

        /// <summary>
        /// Ograniczenie do wybranych ofert Allegro (id ofert). Pusta lista = wszystkie oferty
        /// z wybranych cenników. Na start testujemy na kilku ofertach.
        /// </summary>
        public List<string> TestOfferIds { get; set; } = new();

        /// <summary>Ile nowych ofert wolno założyć w jednym cyklu. 0 = bez limitu.</summary>
        public int MaxNewOffersPerCycle { get; set; } = 5;

        /// <summary>
        /// Ile ofert wolno w jednym cyklu zaktualizować treściowo (opis, atrybuty, zdjęcia, wymiary...).
        /// 0 = bez limitu (domyślnie) - InPost pozwala na 10 zapytań na sekundę, a klient czeka, gdy limit się wyczerpie.
        /// </summary>
        public int MaxContentUpdatesPerCycle { get; set; }

        /// <summary>
        /// Cenniki dostawy Allegro, z których oferty trafiają do InPost - każdy z dostawcą, którego
        /// produkty za nim stoją, i marką dla produktów bez rozpoznanej marki.
        /// </summary>
        public List<DeliveryPriceListSettings> DeliveryPriceLists { get; set; } = new();

        /// <summary>Minimalny stan produktu u dostawcy, przy którym oferta jest wystawiona.</summary>
        public int MinProductStock { get; set; } = 2;
    }

    /// <summary>
    /// Cena oferty InPost liczona od ceny zakupu - tak jak na Allegro: marża własna, prowizja
    /// platformy, dopłaty za gabaryt i towar niestandardowy oraz koszt wysyłki.
    /// </summary>
    public class InPostPriceSettings
    {
        /// <summary>Marża własna wg przedziałów ceny zakupu brutto (jak w serwisie Rolmar).</summary>
        public List<MarginRange> MarginRanges { get; set; } = new();

        /// <summary>Prowizja InPost (%) liczona od ceny oferty. 0 = brak prowizji.</summary>
        public decimal CommissionPercent { get; set; }

        /// <summary>
        /// Koszt wysyłki doliczany do każdej oferty (PLN brutto) - stawka, którą InPost pobiera
        /// od sprzedawcy za przesyłkę. API nie pozwala ustawić ceny dostawy per oferta.
        /// </summary>
        public decimal ShippingCostGross { get; set; }

        /// <summary>Dopłata za wysyłkę gabarytową (PLN netto) - produkt z typem dostawy 1.</summary>
        public decimal BulkyDeliveryPriceNet { get; set; }

        /// <summary>Dopłata za wysyłkę niestandardową (PLN netto) - produkt z typem dostawy 2.</summary>
        public decimal CustomDeliveryPriceNet { get; set; }

        /// <summary>Dopłata (PLN brutto) za produkt, którego bok przekracza <see cref="OversizeThresholdCm"/>.</summary>
        public decimal OversizeSurcharge { get; set; }

        public decimal OversizeThresholdCm { get; set; } = 150m;

        /// <summary>Stawka VAT w procentach - do przeliczenia dopłat netto i do pola taxRateInfo.</summary>
        public int VatRate { get; set; } = 23;
    }

    public class InPostOfferSettings
    {
        /// <summary>
        /// Ręczne mapowanie kategorii Allegro na kategorię-liść InPost - ma pierwszeństwo przed
        /// dopasowaniem po nazwach. Klucz to id kategorii Allegro (oferty albo dowolnego jej
        /// przodka), wartość - UUID kategorii InPost. Pozostałe kategorie dopasowujemy po ścieżce
        /// nazw, bo drzewo InPost powtarza nazwy Allegro.
        /// </summary>
        public Dictionary<string, string> CategoryMap { get; set; } = new();

        /// <summary>
        /// Gałęzie, które w Allegro i InPost nazywają się inaczej - podmieniany jest początek ścieżki
        /// Allegro, a dalej działa dopasowanie po nazwach. Np. "Motoryzacja > Chemia" →
        /// "Motoryzacja > Chemia samochodowa".
        /// </summary>
        public List<CategoryPathAlias> CategoryPathAliases { get; set; } = new();

        /// <summary>
        /// Jakość części (zgodnie z GVO), gdy dostawca jej nie podał - wartość jak u Rolmara
        /// dla zamienników.
        /// </summary>
        public string DefaultPartQuality { get; set; } = "P - zamiennik o jakości porównywalnej do oryginału";

        /// <summary>Reguły marki: słowo w nazwie produktu albo w ścieżce kategorii dostawcy → marka.</summary>
        public List<BrandRule> BrandRules { get; set; } = new();

        /// <summary>Dni na wysyłkę, gdy oferta Allegro nie ma czasu realizacji.</summary>
        public int DefaultDaysToShip { get; set; } = 2;

        public string ReturnPolicy { get; set; } = string.Empty;
        public string ComplaintPolicy { get; set; } = string.Empty;

        /// <summary>
        /// Czy wysyłać sekcję GPSR (producent, osoba odpowiedzialna, informacje o bezpieczeństwie).
        /// Kategorie części rolniczych jej nie wymagają, ale pełniejsza oferta jest lepsza -
        /// sekcja idzie tylko wtedy, gdy ma komplet wymaganych pól.
        /// </summary>
        public bool SendGpsr { get; set; } = true;

        public string SafetyInformation { get; set; } = string.Empty;

        /// <summary>Limity paczki standardowej InPost: bok i suma boków (cm), waga (kg).</summary>
        public decimal MaxSideCm { get; set; } = 120m;
        public decimal MaxDimensionsSumCm { get; set; } = 220m;
        public decimal MaxWeightKg { get; set; } = 30m;

        public int MaxImages { get; set; } = 20;
    }

    public class DeliveryPriceListSettings
    {
        /// <summary>Nazwa cennika dostawy na Allegro.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Dostawca, którego produkty stoją za ofertami z tego cennika.</summary>
        public IntegrationCompany IntegrationCompany { get; set; }

        /// <summary>Marka dla produktów, których marki nie udało się rozpoznać (Rolmar → "Rolmar", Gąska → "JAG").</summary>
        public string DefaultBrand { get; set; } = string.Empty;

        /// <summary>
        /// Wymiary paczki (cm) dla produktów bez wymiarów w specyfikacji. InPost bez wymiarów
        /// odrzuca ofertę (DELIVERY_METHOD_NOT_VALID) i na ich podstawie dobiera sposób dostawy,
        /// więc podajemy największą paczkę, jaką dopuszcza ten cennik na Allegro.
        /// 0 = brak zapasowych wymiarów.
        /// </summary>
        public decimal FallbackLengthCm { get; set; }
        public decimal FallbackWidthCm { get; set; }
        public decimal FallbackHeightCm { get; set; }
    }

    public class CategoryPathAlias
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
    }

    public class BrandRule
    {
        public string Keyword { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
    }
}
