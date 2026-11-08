using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.Helpers;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    public class AllegroParametersService : IAllegroParametersService
    {
        private readonly ILogger<AllegroParametersService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly ICategoryRepository _categoryRepo;
        private readonly IParameterRepository _parameterRepo;
        private readonly ServiceContext _service;

        public AllegroParametersService(IProductRepository productRepo, ICategoryRepository categoryRepo, IParameterRepository parameterRepository,
            ServiceContext serviceContext, ILogger<AllegroParametersService> logger)
        {
            _service = serviceContext;
            _productRepo = productRepo;
            _categoryRepo = categoryRepo;
            _parameterRepo = parameterRepository;
            _logger = logger;
        }

        public async Task UpdateParameters(CancellationToken ct = default)
        {
            try
            {
                // Parametry produktu bierzemy z produktu z katalogu Allegro - liczymy tylko
                // obowiązkowe parametry oferty, a uzupełnianie hurtowe (m.in. "Strona zabudowy",
                // parametr produktu) pomijamy.
                var offerParametersOnly = _service.ProductParametersFromCatalog;

                if (!offerParametersOnly)
                {
                    // Parametry niezależne od danych produktu uzupełniamy w bazie, bo zwykły krok
                    // omija produkty, które mają już jakiekolwiek parametry - a brakujący parametr
                    // obowiązkowy blokuje wystawienie oferty w każdym cyklu.
                    var filled = await _parameterRepo.FillDataIndependentParametersAsync(
                        ParameterDefaults.CountInOfferSqlPattern,
                        ParameterDefaults.CountInOfferValue,
                        ParameterDefaults.MountingSideParameterName,
                        ParameterDefaults.UniversalMountingSidesOrdered,
                        ct);

                    if (filled > 0)
                        _logger.LogInformation("Data-independent parameters filled in: {Count}.", filled);
                }

                // Produkty bez parametrów i te, którym brakuje parametru obowiązkowego w kategorii
                // domyślnej albo w kategorii, w której faktycznie stoi ich oferta.
                var products = await _productRepo.GetProductsToUpdateParameters(ct);

                if (products.Count == 0)
                    return;

                var targetCategories = await _productRepo.GetParameterTargetCategories(ct);
                var categoryParamsCache = new Dictionary<int, List<CategoryParameter>>();
                var allProductParameters = new List<ProductParameter>();
                var missing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var product in products)
                {
                    try
                    {
                        var categories = targetCategories.TryGetValue(product.Id, out var known)
                            ? new HashSet<int>(known)
                            : new HashSet<int>();

                        if (product.DefaultAllegroCategory != 0)
                            categories.Add(product.DefaultAllegroCategory);

                        foreach (var categoryId in categories)
                        {
                            if (!categoryParamsCache.TryGetValue(categoryId, out var categoryParams))
                            {
                                categoryParams = (await _categoryRepo.GetCategoryParametersAsync(categoryId, ct)).ToList();
                                categoryParamsCache[categoryId] = categoryParams;
                            }

                            foreach (var catParam in categoryParams)
                            {
                                if (offerParametersOnly && !IsRequiredOfferParameter(catParam))
                                    continue;

                                var value = MapProductToParameter(product, catParam);

                                if (string.IsNullOrEmpty(value))
                                {
                                    if (catParam.Required || catParam.RequiredForProduct)
                                    {
                                        var key = $"{catParam.Name} ({categoryId})";
                                        missing[key] = missing.GetValueOrDefault(key) + 1;
                                        _logger.LogDebug("Missing required parameter {ParamName} in category {CategoryId} for product {Code}",
                                            catParam.Name, categoryId, product.Code);
                                    }

                                    continue;
                                }

                                allProductParameters.Add(new ProductParameter
                                {
                                    ProductId = product.Id,
                                    CategoryParameterId = catParam.Id,
                                    // O sekcji decyduje wyłącznie DescribesProduct. Parametr opisujący
                                    // produkt Allegro przyjmuje tylko w sekcji produktu - wysłany przy
                                    // ofercie wraca błędem "should not be specified as in section offer",
                                    // nawet gdy kategoria oznacza go jako wymagany.
                                    IsForProduct = catParam.DescribesProduct,
                                    Value = value
                                });
                            }
                        }
                    }
                    catch (Exception exProduct)
                    {
                        _logger.LogError(exProduct, "Error updating parameters for product {Code}", product.Code);
                    }
                }

                // Zapis dokłada tylko brakujące parametry - istniejących (np. poprawionych według
                // komunikatu Allegro) nie nadpisuje.
                if (allProductParameters.Count > 0)
                    await _parameterRepo.SaveProductParametersAsync(allProductParameters, ct);

                // Produkt z parametrem, którego nie umiemy wyliczyć, wróci dopiero po zmianie
                // produktu albo parametrów kategorii - nie w każdym cyklu.
                await _productRepo.SetParametersChecked(products.Select(p => p.Id), ct);

                _logger.LogInformation("Product parameters checked for {ProductsCount} products, {Count} parameter values computed.",
                    products.Count, allProductParameters.Count);

                if (missing.Count > 0)
                {
                    _logger.LogWarning("Required parameters that could not be filled (parameter (category): products): {Missing}",
                        string.Join("; ", missing.OrderByDescending(m => m.Value).Take(15).Select(m => $"{m.Key}: {m.Value}")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error updating parameters for products.");
            }
        }

        /// <summary>
        /// Obowiązkowy parametr oferty - jedyny, który wypełniamy sami, gdy parametry produktu
        /// pochodzą z katalogu Allegro. Parametr opisujący produkt dostarcza produkt z katalogu.
        /// </summary>
        internal static bool IsRequiredOfferParameter(CategoryParameter param) => param.Required && !param.DescribesProduct;

        /// <summary>
        /// Wartość parametru dla produktu. Wartość słownikowa musi istnieć w słowniku kategorii -
        /// spoza słownika Allegro odrzuca całą ofertę. Parametr obowiązkowy, którego nie da się
        /// wyliczyć, dostaje wartość niejednoznaczną ("Inna", "nieznany producent").
        /// </summary>
        internal static string? MapProductToParameter(RolmarProduct product, CategoryParameter param)
        {
            if (param == null) return null;

            var value = MapKnownValue(product, param);
            var fitted = ParameterFallbacks.FitToDictionary(param, value);

            // Słownik z własnymi wartościami przyjmie też wartość spoza listy.
            if (fitted == null && !string.IsNullOrWhiteSpace(value) && param.CustomValuesEnabled)
                fitted = value;

            if (fitted == null && (param.Required || param.RequiredForProduct))
                fitted = ParameterFallbacks.ForRequired(param, product);

            return fitted;
        }

        private static string? MapKnownValue(RolmarProduct product, CategoryParameter param)
        {
            var name = param.Name?.Trim().ToLowerInvariant();

            var directMappings = new Dictionary<string, Func<RolmarProduct, string?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["stan"] = _ => "Nowy",
                ["waga produktu z opakowaniem jednostkowym"] = p => p.Weight.ToString(), // bez ?. dla float
                ["numer katalogowy części"] = p => p.Code,
                // Bez tych dwóch mapowań parametry wymagane dla produktu zostawały puste,
                // a Allegro odrzucało ofertę komunikatem "Uzupełnij parametry obowiązkowe".
                ["ean (gtin)"] = p => p.Ean,
                ["kod producenta"] = p => p.Code,
                ["typ maszyny"] = _ => "Inny",
                ["rodzaj skrzyni"] = _ => "Brak informacji",
                ["typ samochodu"] = _ => "Niezdefiniowany",
                ["numer katalogowy oryginału"] = p => p.Code,
                ["numery katalogowe zamienników"] = p => p.Substitutes,
                ["stan opakowania"] = _ => "oryginalne",
                ["jakość części (zgodnie z gvo)"] = _ => "P - zamiennik o jakości porównywalnej do oryginału"
            };

            if (name != null && directMappings.TryGetValue(name, out var resolver))
                return resolver(product);

            // "Liczba ... w ofercie" (tarcz, klocków, sztuk) i "Liczba sztuk w opakowaniu": sprzedajemy
            // jedną pozycję w opakowaniu dostawcy, a dokładniejszej liczby dane dostawcy nie podają.
            if (ParameterDefaults.IsCountInOffer(name) || ParameterFallbacks.IsPackageCount(name))
                return ParameterDefaults.CountInOfferValue;

            // Strony zabudowy nie znamy, więc zamiast zgadywać "przód" albo "lewa" bierzemy
            // z listy dopuszczalnych wartości tę, która nie zawęża zastosowania. Gdy kategoria
            // żadnej takiej nie ma, zostawiamy parametr pusty - zmyślony bok wprowadzałby w błąd.
            if (name == "strona zabudowy")
                return ParameterDefaults.UniversalMountingSide(param.Values?.Select(v => v.Value));

            if (name == "producent" || name == "producent części")
                return GetMatchingValue(product, param);

            // "Marka" to marka samego produktu (słownik ma np. "Rolmar", "bez marki"),
            // "Marka maszyny" - marka maszyny, do której pasuje część.
            if (name == "marka")
                return GetProductBrandValue(product, param);

            if (name == "marka maszyny")
                return GetBrandMatchingValue(product, param);

            return null;
        }

        /// <summary>
        /// Producent części: marka dostawcy (SupplierName), w słowniku dopasowana bez względu na
        /// wielkość liter i polskie znaki. Brak dopasowania zostawia decyzję wartości zastępczej.
        /// </summary>
        private static string? GetMatchingValue(RolmarProduct product, CategoryParameter param)
        {
            if (string.IsNullOrWhiteSpace(product.SupplierName))
                return null;

            var supplier = product.SupplierName.Trim();

            // "Premium" to w słowniku linia produktów, nie producent - nie dopasowujemy jej.
            if (string.Equals(supplier, "Premium", StringComparison.OrdinalIgnoreCase))
                return null;

            return string.Equals(supplier, "rollon-solid", StringComparison.OrdinalIgnoreCase) ? "Rollon" : supplier;
        }

        /// <summary>
        /// Marka produktu: marka dostawcy (SupplierName). Pole tekstowe dostaje ją wprost, słownik -
        /// tylko gdy ją zna; inaczej parametr obowiązkowy dostanie "bez marki" / wartość niejednoznaczną.
        /// </summary>
        private static string? GetProductBrandValue(RolmarProduct product, CategoryParameter param) =>
            ParameterFallbacks.FitToDictionary(param, product.SupplierName?.Trim());

        /// <summary>
        /// Marka maszyny: marki z drzewa zastosowań produktu (pierwsza znana w słowniku), potem marka
        /// dostawcy. Gdy żadna nie pasuje, parametr obowiązkowy dostaje wartość niejednoznaczną.
        /// </summary>
        private static string? GetBrandMatchingValue(RolmarProduct product, CategoryParameter param)
        {
            var candidates = (product.Applications ?? new List<ProductApplication>())
                .Where(a => a.ParentID == 0)
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            foreach (var candidate in candidates)
            {
                var match = ParameterFallbacks.FitToDictionary(param, candidate);
                if (match != null)
                    return match;
            }

            // Marka dostawcy bywa też marką maszyny (np. Ursus) - ale tylko gdy słownik ją zna.
            // Do pola tekstowego jej nie wpisujemy: producent części to nie marka maszyny.
            return ParameterFallbacks.IsDictionary(param)
                ? ParameterFallbacks.FitToDictionary(param, product.SupplierName)
                : null;
        }
    }
}
