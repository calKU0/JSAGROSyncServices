using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    public class AllegroParametersService : IAllegroParametersService
    {
        private readonly ILogger<AllegroParametersService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly ICategoryRepository _categoryRepo;
        private readonly IParameterRepository _parameterRepo;

        public AllegroParametersService(IProductRepository productRepo, ICategoryRepository categoryRepo, IParameterRepository parameterRepository, ILogger<AllegroParametersService> logger)
        {
            _productRepo = productRepo;
            _categoryRepo = categoryRepo;
            _parameterRepo = parameterRepository;
            _logger = logger;
        }

        public async Task UpdateParameters(CancellationToken ct = default)
        {
            try
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

                var products = await _productRepo.GetProductsToUpdateParameters(ct);

                // Cache category parameters
                var categoryParamsCache = new Dictionary<int, List<CategoryParameter>>();

                // Collect all parameter inserts before saving
                var allProductParameters = new List<ProductParameter>();

                foreach (var product in products)
                {
                    try
                    {
                        if (!categoryParamsCache.TryGetValue(product.DefaultAllegroCategory, out var categoryParams))
                        {
                            categoryParams = (await _categoryRepo.GetCategoryParametersAsync(product.DefaultAllegroCategory, ct)).ToList();
                            categoryParamsCache[product.DefaultAllegroCategory] = categoryParams;
                        }

                        var productParams = new List<ProductParameter>();

                        foreach (var catParam in categoryParams)
                        {
                            var value = MapProductToParameter(product, catParam);

                            if (string.IsNullOrEmpty(value) && (catParam.Required || catParam.RequiredForProduct))
                            {
                                _logger.LogWarning("Missing required parameter {ParamName} for product {Code}", catParam.Name, product.Code);
                                continue;
                            }

                            productParams.Add(new ProductParameter
                            {
                                ProductId = product.Id,
                                CategoryParameterId = catParam.Id,
                                // Parametr wymagany na ofercie musi z nią pojechać, nawet jeśli
                                // opisuje produkt - inaczej Allegro odrzuca ofertę komunikatem
                                // "Uzupełnij parametry obowiązkowe", choć wartość mamy zapisaną.
                                IsForProduct = catParam.DescribesProduct && !catParam.Required,
                                Value = value!
                            });
                        }

                        if (productParams.Count > 0)
                        {
                            allProductParameters.AddRange(productParams);
                            _logger.LogDebug("Assigned {Count} parameters for product {Code}", productParams.Count, product.Code);
                        }
                    }
                    catch (Exception exProduct)
                    {
                        _logger.LogError(exProduct, "Error updating parameters for product {Code}", product.Code);
                    }
                }

                // Single bulk save at the end instead of per product
                if (allProductParameters.Any())
                {
                    await _parameterRepo.SaveProductParametersAsync(allProductParameters, ct);
                    _logger.LogInformation("Product parameters saved: {Count} for {ProductsCount} products.", allProductParameters.Count, products.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error updating parameters for products.");
            }
        }

        private string? MapProductToParameter(RolmarProduct product, CategoryParameter param)
        {
            if (param == null) return null;

            var name = param.Name?.ToLowerInvariant();

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

            // "Liczba ... w ofercie" (tarcz, klocków, sztuk): sprzedajemy jedną pozycję
            // w opakowaniu dostawcy, a dokładniejszej liczby dane dostawcy nie podają.
            if (ParameterDefaults.IsCountInOffer(name))
                return ParameterDefaults.CountInOfferValue;

            // Strony zabudowy nie znamy, więc zamiast zgadywać "przód" albo "lewa" bierzemy
            // z listy dopuszczalnych wartości tę, która nie zawęża zastosowania. Gdy kategoria
            // żadnej takiej nie ma, zostawiamy parametr pusty - zmyślony bok wprowadzałby w błąd.
            if (name == "strona zabudowy")
                return ParameterDefaults.UniversalMountingSide(param.Values?.Select(v => v.Value));

            if (name == "producent" || name == "producent części")
                return GetMatchingValue(product, param);

            if (name == "marka" || name == "marka maszyny")
                return GetBrandMatchingValue(product, param);

            return null;
        }

        private string? GetMatchingValue(RolmarProduct product, CategoryParameter param)
        {
            const string fallback = "nieznany producent";

            if (param?.Type?.Equals("dictionary", StringComparison.OrdinalIgnoreCase) == true
                && param.Values?.Any() == true)
            {
                var dict = param.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v.Value) && v.Value != "Premium")
                    .Select(v => new { Raw = v.Value, Normalized = Normalize(v.Value) })
                    .ToList();

                var dictSet = new HashSet<string>(dict.Select(d => d.Normalized).OfType<string>());

                // 1. SupplierName exact match
                if (!string.IsNullOrEmpty(product.SupplierName))
                {
                    var supplier = Normalize(product.SupplierName);
                    var match = dict.FirstOrDefault(v => v.Normalized == supplier);
                    if (match != null) return match.Raw;
                }

                return fallback;
            }

            return !string.IsNullOrEmpty(product.SupplierName)
                ? Normalize(product.SupplierName)
                : fallback;
        }

        private string? GetBrandMatchingValue(RolmarProduct product, CategoryParameter param)
        {
            const string fallback = "Inna";

            if (product.Applications == null || !product.Applications.Any())
                return fallback;

            var rootBrands = product.Applications
                .Where(a => a.ParentID == 0)
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (!rootBrands.Any())
                return fallback;

            if (param?.Type?.Equals("dictionary", StringComparison.OrdinalIgnoreCase) == true && param.Values?.Any() == true)
            {
                var dictValues = param.Values.Select(v => v.Value).ToList();

                var brandMatch = dictValues.FirstOrDefault(dv => rootBrands.Any(rb => dv.Equals(rb, StringComparison.OrdinalIgnoreCase)));

                if (!string.IsNullOrEmpty(brandMatch)) return brandMatch;

                if (!string.IsNullOrEmpty(product.SupplierName))
                {
                    var supplierMatch = dictValues.FirstOrDefault(dv => dv.Equals(product.SupplierName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(supplierMatch)) return supplierMatch;
                }

                return fallback;
            }

            return rootBrands.FirstOrDefault() ?? fallback;
        }

        private static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var lowered = value.Trim().ToLowerInvariant();
            return lowered == "rollon-solid" ? "rollon" : lowered;
        }
    }
}