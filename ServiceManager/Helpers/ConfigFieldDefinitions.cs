using ServiceManager.Enums;
using ServiceManager.Models;
using ServiceManager.Validation;

namespace ServiceManager.Helpers
{
    public static class ConfigFieldDefinitions
    {
        public static readonly List<ConfigField> AllFields = new()
        {
            // Dane dostępowe do API są ustawiane przy wdrożeniu i nie pokazujemy ich w konfiguratorze.
            // Gąska API
            new ConfigField { Key = "GaskaApiCredentials:BaseUrl", Label = "Adres API Gąska", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Acronym", Label = "Akronim", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Person", Label = "Osoba", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Password", Label = "Hasło", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Key", Label = "Klucz API", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductsPerPage", Label = "Produkty na stronę", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductsInterval", Label = "Interwał pobierania produktów", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductPerDay", Label = "Produkty dziennie", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductInterval", Label = "Interwał pobierania szczegółów", Group = "Gąska API", IsEnabled = false, IsVisable = false },

            // Inter Cars API
            new ConfigField { Key = "InterCarsApiCredentials:BaseUrl", Label = "Adres API Inter Cars", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:AuthUrl", Label = "Adres autoryzacji", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:ClientId", Label = "Client ID", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:ClientSecret", Label = "Client Secret", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:Warehouses", Label = "Magazyny do sumowania stanów", Group = "Inter Cars API", FieldType = ConfigFieldType.StringList, Description = "Stan produktu to suma dostępności z tych magazynów Inter Cars." },
            new ConfigField { Key = "InterCarsApiCredentials:ProductDetailsPerRun", Label = "Limit szczegółów na przebieg", Group = "Inter Cars API", FieldType = ConfigFieldType.Int, IsVisable = true, Description = "Ile szczegółów produktów pobrać w jednym cyklu. Najpierw produkty bez szczegółów, potem najdawniej odświeżane. 0 = bez limitu, czyli cały katalog w jednym cyklu." },
            new ConfigField { Key = "InterCarsApiCredentials:ProductDetailsRefreshDays", Label = "Co ile dni odświeżać szczegóły produktu", Group = "Inter Cars API", FieldType = ConfigFieldType.Int, Description = "Waga, wymiary i EAN przychodzą po jednym produkcie na zapytanie. Najpierw pobierane są produkty bez szczegółów, potem te odświeżane dawniej niż podana liczba dni temu." },
            new ConfigField { Key = "InterCarsApiCredentials:DataBaseUrl", Label = "Adres wymiany plików CSV", Group = "Inter Cars pliki CSV", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:DataCustomerNumber", Label = "Numer klienta (katalog z plikami)", Group = "Inter Cars pliki CSV", Description = "Nazwa katalogu z plikami CSV na serwerze wymiany Inter Cars." },
            new ConfigField { Key = "InterCarsApiCredentials:DataUser", Label = "Użytkownik wymiany plików", Group = "Inter Cars pliki CSV", IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:DataPassword", Label = "Hasło wymiany plików", Group = "Inter Cars pliki CSV", IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:Parallelism", Label = "Równoległych zapytań", Group = "Inter Cars API", FieldType = ConfigFieldType.Int, IsVisable = false },

            // Allegro API
            new ConfigField { Key = "AllegroApiCredentials:BaseUrl", Label = "Adres API Allegro", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:AuthBaseUrl", Label = "Adres Autoryzacji Allegro", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientName", Label = "Nazwa klienta", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientId", Label = "Client ID", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientSecret", Label = "Client Secret", Group = "Allegro API", IsEnabled = false, IsVisable = false },

            // ERLI API
            new ConfigField { Key = "ErliApiCredentials:BaseUrl", Label = "Adres API Erli", Group = "Erli API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "ErliApiCredentials:ApiKey", Label = "Klucz API", Group = "Erli API", IsEnabled = false, IsVisable = false },

            // Courier Settings
            new ConfigField { Key = "CourierSettings:DpdFinalOrderHour", Label = "Ostateczna godzina DPD", Group = "Ustawienia Kurierów", IsEnabled = true, FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "CourierSettings:FedexFinalOrderHour", Label = "Ostateczna godzina Fedex", Group = "Ustawienia Kurierów", IsEnabled = true, FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "CourierSettings:GlsFinalOrderHour", Label = "Ostateczna godzina GLS", Group = "Ustawienia Kurierów", IsEnabled = true, FieldType = ConfigFieldType.Int },

            // Price Settings
            new ConfigField { Key = "PriceSettings:AllegroMarginUnder5PLN", Label = "Prowizja allegro poniżej 5 PLN", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:AllegroMarginBetween5and1000PLNPercent", Label = "Prowizja allegro 5-1000 PLN (%)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:AllegroMarginMoreThan1000PLN", Label = "Prowizja allegro powyżej 1000 PLN", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:BulkyDeliveryPriceNet", Label = "Cena netto wysyłki gabarytowej", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:CustomDeliveryPriceNet", Label = "Cena netto wysyłki niestandardowej", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:DropshippingPriceNet", Label = "Cena netto dropshippingu", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:OversizeSurcharge", Label = "Dopłata za produkt ponadgabarytowy (PLN)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Doliczana do ceny oferty, gdy którykolwiek wymiar produktu przekracza próg. 0 = bez dopłaty." },
            new ConfigField { Key = "PriceSettings:OversizeThresholdCm", Label = "Próg ponadgabarytu (cm)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField
            {
                Key = "PriceSettings:MarginRanges",
                Label = "Marża własna wg ceny zakupu brutto",
                Group = "Narzuty",
                FieldType = ConfigFieldType.Table,
                AddButtonText = "Dodaj zakres",
                SortBy = "Min",
                TableValidator = MarginRangeValidator.Validate,
                Description = "Marża doliczana do ceny zakupu brutto z przedziału, w który cena wpada. Zakresy zapisują się posortowane od najniższego; cena powyżej ostatniego zakresu dostaje marżę ostatniego.",
                Columns =
                {
                    new TableColumn { Property = "Min", Header = "Od (PLN)", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "Max", Header = "Do (PLN)", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "Margin", Header = "Marża (%)", Kind = TableColumnKind.Decimal }
                }
            },
            new ConfigField { Key = "PriceSettings:MaxPriceDropPercent", Label = "Maksymalny spadek ceny (%)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Dotyczy ceny zakupu u dostawcy. Po skokowym spadku powyżej tej wartości cena oferty nie jest aktualizowana, a produkt nie jest wystawiany - zgłaszamy to w logu i mailem. Blokada znika, gdy cena zakupu wróci do poprzedniego poziomu. 0 = bez ograniczenia." },

            // AppSettings
            new ConfigField { Key = "AppSettings:StartHour", Label = "Godzina rozpoczęcia synchronizacji", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int},
            new ConfigField { Key = "AppSettings:EndHour", Label = "Godzina zakończenia synchronizacji", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int},
            new ConfigField { Key = "AppSettings:CategoriesId", Label = "Synchronizowane kategorie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.IntList, CategoryCompany = 2, Description = "Pobierane są i trafiają na Allegro tylko produkty z tych kategorii (razem z podkategoriami). Usunięcie kategorii kończy (ENDED) oferty jej produktów." },
            new ConfigField { Key = "AppSettings:CategoriesName", Label = "Synchronizowane kategorie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, CategoryCompany = 1, Description = "Pobierane są i trafiają na Allegro tylko produkty z tych kategorii (razem z podkategoriami). Usunięcie kategorii kończy (ENDED) oferty jej produktów." },
            new ConfigField { Key = "AppSettings:CategoriesKey", Label = "Synchronizowane kategorie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, CategoryCompany = 3, Description = "Pobierane są i trafiają na Allegro tylko produkty z tych kategorii (razem z podkategoriami), i tylko te, które są w plikach CSV Inter Cars - a te zawierają wyłącznie asortyment rolniczy (stąd AGRO na początku ścieżki). Usunięcie kategorii kończy (ENDED) oferty jej produktów." },
            new ConfigField { Key = "AppSettings:DeliveriesWithoutPriceUpdate", Label = "Cenniki dostaw zarządzane ręcznie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Oferty z tych cenników mają aktualizowany tylko stan i opis. Serwis nie zmienia im ceny, cennika dostawy ani czasu realizacji." },
            new ConfigField { Key = "AppSettings:PriceDropAlertEmail", Label = "Email do powiadomień o spadku ceny", IsVisable = false, Group = "Ustawienia serwisu" },
            new ConfigField { Key = "AppSettings:UploadParallelism", Label = "Ile produktów wysyłać równolegle", IsVisable = false, Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:CourierPriceSurcharge", Label = "Dopłata do ceny przy wysyłce kurierem (PLN)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:ArchiveAfterDaysMissing", Label = "Po ilu dniach braku u dostawcy zakończyć ofertę", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int, Description = "Produkt, którego dostawca nie zwraca od tylu dni, jest oznaczany jako archiwalny: nie trafia na Allegro, a jego oferta jest kończona (ENDED). Karencja chroni przed zakończeniem ofert całego asortymentu przez jedno nieudane pobranie od dostawcy." },
            new ConfigField { Key = "AppSettings:AllegroParallelism", Label = "Równoległe zapytania do Allegro", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int, Description = "Ile zapytań do Allegro naraz. Za dużo kończy się błędami 429 i ponowieniami, które i tak spowalniają cykl. Zalecane 4-10." },
            new ConfigField { Key = "AppSettings:MinProductStock", Label = "Minimalny stan produktu", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:MinProductPriceNet", Label = "Minimalna cena netto produktu (u dostawcy)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:BundleProductsUnderPriceNet", Label = "Łącz w zestawy poniżej X PLN", IsVisable = false, Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:LogsExpirationDays", Label = "Ilość dni zachowania logów", IsVisable = false, Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:FetchIntervalMinutes", Label = "Co ile wywoływać synchronizację (min)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:OfferProcessingDelayMinutes", Label = "Opóźnienie złożenia zamówienia (min)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:NotificationsEmail", Label = "Adresy email do powiadomień (rodzielone średnikiem)", IsVisable = false, Group = "Ustawienia serwisu" },
            new ConfigField { Key = "AppSettings:AllegroDeliveryNames", Label = "Nazwy cenników dostawy Allegro", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Serwis obsługuje tylko zamówienia z tych cenników. Oferty wystawione ręcznie, spoza listy, są pomijane." },
            new ConfigField
            {
                Key = "AppSettings:Deliveries",
                Label = "Cenniki dostaw Allegro",
                Group = "Dostawy",
                FieldType = ConfigFieldType.Table,
                AddButtonText = "Dodaj dostawę",
                TableValidator = DeliveryValidator.Validate,
                Description = "Produkt trafia do najmniejszego cennika, w który mieści się wagą (kg) i wymiarami (cm, paczkę można obrócić).",
                Columns =
                {
                    new TableColumn { Property = "Length", Header = "Długość", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "Width", Header = "Szerokość", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "Height", Header = "Wysokość", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "Weight", Header = "Waga", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "DeliveryName", Header = "Nazwa", Width = 2, Required = true },
                    new TableColumn { Property = "IsSmart", Header = "Smart", Kind = TableColumnKind.Bool, DefaultValue = "true", Width = 0.6, Tooltip = "Cennik objęty Allegro Smart - tylko wtedy koszt wysyłki jest doliczany do ceny oferty." }
                }
            },
            new ConfigField { Key = "AppSettings:DeliveryAddressEmail", Label = "Email podawany w adresie dostawy", IsVisable = false, Group = "Ustawienia serwisu" },

            // Allegro Settings
            new ConfigField { Key = "AllegroSettings:AllegroHandlingTime", Label = "Czas realizacji", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroHandlingTimeCustomProducts", Label = "Czas realizacji produktów niestandardowych", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroSafetyMeasures", Label = "Tekst bezpieczeństwa", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroWarranty", Label = "Nazwa polityki gwarancji", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroReturnPolicy", Label = "Nazwa polityki zwrotów", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroImpliedWarranty", Label = "Nazwa polityki reklamacji", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroResponsiblePerson", Label = "Odpowiedzialna osoba", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroResponsibleProducer", Label = "Odpowiedzialny producent", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:DefaultPartsManufacturer", Label = "Domyślny producent części", Group = "Ustawienia Allegro", Description = "Wstawiany w parametr „Producent części”, gdy dostawca go nie podaje." },

            // ============================================================ InPost Von Halsky
            // Dane dostępowe ustawiane przy wdrożeniu - jak przy pozostałych API, niewidoczne w konfiguratorze.
            new ConfigField { Key = "InPostApiCredentials:BaseUrl", Label = "Adres API InPost", Group = "InPost API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InPostApiCredentials:TokenUrl", Label = "Adres tokenu", Group = "InPost API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InPostApiCredentials:ClientId", Label = "Client ID", Group = "InPost API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InPostApiCredentials:ClientSecret", Label = "Client Secret", Group = "InPost API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InPostApiCredentials:OrganizationId", Label = "ID organizacji (sklepu)", Group = "InPost API", IsEnabled = false, IsVisable = false },

            // Wysyłka do InPost
            new ConfigField { Key = "AppSettings:DryRun", Label = "Tryb próbny (bez wysyłki do InPost)", Group = "Wysyłka do InPost", FieldType = ConfigFieldType.Bool, Description = "Zaznaczone: serwis buduje oferty i zapisuje w logu, ile jest gotowych i dlaczego reszta odpadła, ale niczego nie wysyła do InPost." },
            new ConfigField { Key = "AppSettings:MaxClosePercentPerCycle", Label = "Bezpiecznik: maks. % ofert zamykanych w cyklu", Group = "Wysyłka do InPost", FieldType = ConfigFieldType.Decimal, Description = "Gdy w jednym cyklu do zamknięcia wychodzi więcej ofert (np. chwilowo zerowe stany u dostawcy albo zła mapa kategorii), serwis nic nie zamyka i zgłasza błąd w logu. Poniżej 10 ofert bezpiecznik nie działa. 0 = wyłączony." },
            new ConfigField { Key = "AppSettings:MaxNewOffersPerCycle", Label = "Ile nowych ofert zakładać na cykl", Group = "Wysyłka do InPost", FieldType = ConfigFieldType.Int, Description = "0 = bez limitu. Mniejsza liczba pozwala wypuszczać oferty partiami." },
            new ConfigField { Key = "AppSettings:MaxContentUpdatesPerCycle", Label = "Ile ofert aktualizować treściowo na cykl", Group = "Wysyłka do InPost", FieldType = ConfigFieldType.Int, Description = "Aktualizacja opisu, atrybutów, zdjęć i wymiarów ofert, w których coś się zmieniło. 0 = bez limitu. Ceny i stany są aktualizowane zawsze, bez limitu." },
            new ConfigField { Key = "AppSettings:TestOfferIds", Label = "Tylko te oferty Allegro (test)", Group = "Wysyłka do InPost", FieldType = ConfigFieldType.StringList, Description = "Numery ofert Allegro. Gdy lista nie jest pusta, serwis obsługuje wyłącznie te oferty i niczego nie zamyka. Pusta lista = wszystkie oferty z cenników poniżej." },
            new ConfigField
            {
                Key = "AppSettings:DeliveryPriceLists",
                Label = "Cenniki dostaw Allegro wysyłane do InPost",
                Group = "Wysyłka do InPost",
                FieldType = ConfigFieldType.Table,
                AddButtonText = "Dodaj cennik",
                Description = "Do InPost trafiają tylko aktywne oferty z tych cenników. Dostawca wskazuje, czyje produkty stoją za ofertami (stąd cena zakupu, stan i dane produktu). Marka domyślna trafia do ofert, w których marki nie da się ustalić. Wymiary paczki (cm) są wysyłane, gdy produkt nie ma wymiarów ani klasy wysyłki od dostawcy - InPost bez wymiarów odrzuca ofertę.",
                Columns =
                {
                    new TableColumn { Property = "Name", Header = "Nazwa cennika", Width = 3, Required = true },
                    new TableColumn { Property = "IntegrationCompany", Header = "Dostawca", Kind = TableColumnKind.Choice, Choices = { "Rolmar", "Gaska", "InterCars" }, Width = 1.3 },
                    new TableColumn { Property = "DefaultBrand", Header = "Marka domyślna", Width = 1.5, Required = true },
                    new TableColumn { Property = "FallbackLengthCm", Header = "Dł. paczki", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "FallbackWidthCm", Header = "Szer.", Kind = TableColumnKind.Decimal },
                    new TableColumn { Property = "FallbackHeightCm", Header = "Wys.", Kind = TableColumnKind.Decimal }
                }
            },

            // Cena InPost (marża własna - progi w tej samej grupie, edytor progów jak w Allegro)
            new ConfigField { Key = "PriceSettings:CommissionPercent", Label = "Prowizja InPost (%)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Doliczana do całej ceny oferty (z wysyłką i dopłatami). 0 = bez prowizji." },
            new ConfigField { Key = "PriceSettings:ShippingCostGross", Label = "Koszt wysyłki doliczany do ceny (PLN brutto)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Stawka, którą InPost pobiera za przesyłkę. API InPost nie pozwala ustawić ceny dostawy w ofercie, więc jest doliczana do ceny produktu. 0 = nie doliczać." },
            new ConfigField { Key = "PriceSettings:VatRate", Label = "Stawka VAT (%)", Group = "Narzuty", FieldType = ConfigFieldType.Int, IsVisable = false },

            // Treść ofert InPost
            new ConfigField { Key = "OfferSettings:DefaultPartQuality", Label = "Jakość części (GVO), gdy dostawca jej nie podał", Group = "Oferty InPost" },
            new ConfigField { Key = "OfferSettings:DefaultDaysToShip", Label = "Dni na wysyłkę (gdy oferta Allegro nie ma czasu realizacji)", Group = "Oferty InPost", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "OfferSettings:ReturnPolicy", Label = "Warunki zwrotu", Group = "Oferty InPost", Multiline = true },
            new ConfigField { Key = "OfferSettings:ComplaintPolicy", Label = "Warunki reklamacji", Group = "Oferty InPost", Multiline = true },
            new ConfigField { Key = "OfferSettings:SendGpsr", Label = "Wysyłaj dane GPSR (producent, osoba odpowiedzialna)", Group = "Oferty InPost", FieldType = ConfigFieldType.Bool, Description = "Sekcja idzie tylko wtedy, gdy producent ma nazwę i e-mail." },
            new ConfigField { Key = "OfferSettings:SafetyInformation", Label = "Informacje o bezpieczeństwie (GPSR)", Group = "Oferty InPost", Multiline = true },
            new ConfigField { Key = "OfferSettings:MaxSideCm", Label = "Maksymalny bok paczki (cm)", Group = "Oferty InPost", FieldType = ConfigFieldType.Decimal, Description = "Limit paczki standardowej InPost. Większe produkty nie są wystawiane." },
            new ConfigField { Key = "OfferSettings:MaxDimensionsSumCm", Label = "Maksymalna suma boków (cm)", Group = "Oferty InPost", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "OfferSettings:MaxWeightKg", Label = "Maksymalna waga (kg)", Group = "Oferty InPost", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "OfferSettings:MaxImages", Label = "Maksymalna liczba zdjęć", Group = "Oferty InPost", FieldType = ConfigFieldType.Int, IsVisable = false },
            new ConfigField
            {
                Key = "OfferSettings:BrandRules",
                Label = "Reguły marek",
                Group = "Oferty InPost",
                FieldType = ConfigFieldType.Table,
                AddButtonText = "Dodaj regułę",
                Description = "Ujednolicają markę: gdy marka od dostawcy, nazwa produktu albo kategoria dostawcy zawiera słowo z lewej kolumny, w ofercie InPost będzie marka z prawej (np. WARYNSKI → Waryński). Wielkość liter i polskie znaki nie mają znaczenia.",
                Columns =
                {
                    new TableColumn { Property = "Keyword", Header = "Słowo", Required = true },
                    new TableColumn { Property = "Brand", Header = "Marka w InPost", Required = true }
                }
            },

            // Kategorie InPost
            new ConfigField
            {
                Key = "OfferSettings:CategoryMap",
                Label = "Mapa kategorii Allegro → InPost",
                Group = "Kategorie InPost",
                FieldType = ConfigFieldType.Table,
                IsDictionary = true,
                AddButtonText = "Dodaj kategorię",
                Description = "Ma pierwszeństwo przed dopasowaniem po nazwach. Lewa kolumna: numer kategorii Allegro (działa też dla wszystkich jej podkategorii). Prawa: kategoria InPost - pełna ścieżka, np. „Dom i ogród > Narzędzia > Pozostałe narzędzia”, albo jej UUID. Kategoria InPost musi być liściem drzewa.",
                Columns =
                {
                    new TableColumn { Property = "AllegroCategoryId", Header = "Kategoria Allegro (numer)", Required = true },
                    new TableColumn { Property = "InPostCategory", Header = "Kategoria InPost (ścieżka lub UUID)", Width = 4, Required = true }
                }
            },
            new ConfigField
            {
                Key = "OfferSettings:CategoryPathAliases",
                Label = "Gałęzie kategorii nazwane inaczej w Allegro i InPost",
                Group = "Kategorie InPost",
                FieldType = ConfigFieldType.Table,
                AddButtonText = "Dodaj alias",
                Description = "Początek ścieżki Allegro z lewej kolumny jest zamieniany na ścieżkę InPost z prawej, a dalej kategorie dopasowują się po nazwach. Np. „Motoryzacja > Chemia” → „Motoryzacja > Chemia samochodowa”.",
                Columns =
                {
                    new TableColumn { Property = "From", Header = "Ścieżka Allegro", Width = 2, Required = true },
                    new TableColumn { Property = "To", Header = "Ścieżka InPost", Width = 2, Required = true }
                }
            },
        };
    }
}