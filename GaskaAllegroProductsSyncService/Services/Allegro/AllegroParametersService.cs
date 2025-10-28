﻿using AllegroGaskaProductsSyncService.Models;
using AllegroGaskaProductsSyncService.Models.Product;
using AllegroGaskaProductsSyncService.Repositories.Interfaces;
using AllegroGaskaProductsSyncService.Services.Allegro.Interfaces;

namespace AllegroGaskaProductsSyncService.Services.Allegro
{
    public class AllegroParametersService : IAllegroParametersService
    {
        private readonly ILogger<AllegroParametersService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly ICategoryRepository _categoryRepo;

        public AllegroParametersService(IProductRepository productRepo, ICategoryRepository categoryRepo, ILogger<AllegroParametersService> logger)
        {
            _productRepo = productRepo;
            _categoryRepo = categoryRepo;
            _logger = logger;
        }

        public async Task UpdateParameters(CancellationToken ct = default)
        {
            try
            {
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
                            string value = MapProductToParameter(product, catParam);

                            if (string.IsNullOrEmpty(value) && (catParam.Required || catParam.RequiredForProduct))
                            {
                                _logger.LogWarning("Missing required parameter {ParamName} for product {Name} ({Code})", product.Name, product.CodeGaska);
                                continue;
                            }

                            productParams.Add(new ProductParameter
                            {
                                ProductId = product.Id,
                                CategoryParameterId = catParam.Id,
                                IsForProduct = catParam.DescribesProduct,
                                Value = value
                            });
                        }

                        if (productParams.Count > 0)
                        {
                            allProductParameters.AddRange(productParams);
                            _logger.LogInformation("Assigned {Count} parameters for product {Name} ({Code})", productParams.Count, product.Name, product.CodeGaska);
                        }
                    }
                    catch (Exception exProduct)
                    {
                        _logger.LogError(exProduct, "Error updating parameters for product {Name} ({Code})", product.Name, product.CodeGaska);
                    }
                }

                // Single bulk save at the end instead of per product
                if (allProductParameters.Any())
                {
                    await _productRepo.SaveProductParametersAsync(allProductParameters, ct);
                    _logger.LogInformation("Saved {Count} parameters for {ProductsCount} products", allProductParameters.Count, products.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error updating parameters for products.");
            }
        }

        private string MapProductToParameter(Product product, CategoryParameter param)
        {
            if (param == null) return null;

            var name = param.Name?.ToLowerInvariant();

            var directMappings = new Dictionary<string, Func<Product, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["stan"] = _ => "Nowy",
                ["waga produktu z opakowaniem jednostkowym"] = p => p.WeightNet.ToString(), // bez ?. dla float
                ["numer katalogowy części"] = p => p.CodeGaska,
                ["typ maszyny"] = _ => "Inny",
                ["rodzaj skrzyni"] = _ => "Brak informacji",
                ["typ samochodu"] = _ => "Niezdefiniowany",
                ["numer katalogowy oryginału"] = p => p.CodeGaska,
                ["numery katalogowe zamienników"] = p => p.CrossNumbers != null ? string.Join(",", p.CrossNumbers.Select(cn => cn.CrossNumberValue)) : null,
                ["stan opakowania"] = _ => "oryginalne",
                ["jakość części (zgodnie z gvo)"] = _ => "P - zamiennik o jakości porównywalnej do oryginału"
            };

            Func<Product, string> resolver;
            if (name != null && directMappings.TryGetValue(name, out resolver))
                return resolver(product);

            if (name == "producent" || name == "producent części")
                return GetMatchingValue(product, param);

            if (name == "marka" || name == "marka maszyny")
                return GetBrandMatchingValue(product, param);

            return null;
        }

        private string GetMatchingValue(Product product, CategoryParameter param)
        {
            const string fallback = "inny";

            if (param?.Type?.Equals("dictionary", StringComparison.OrdinalIgnoreCase) == true
                && param.Values?.Any() == true)
            {
                var dict = param.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v.Value) && v.Value != "Premium")
                    .Select(v => new { Raw = v.Value, Normalized = Normalize(v.Value) })
                    .ToList();

                var dictSet = new HashSet<string>(dict.Select(d => d.Normalized));

                // 1. SupplierName exact match
                if (!string.IsNullOrEmpty(product.SupplierName))
                {
                    var supplier = Normalize(product.SupplierName);
                    var match = dict.FirstOrDefault(v => v.Normalized == supplier);
                    if (match != null) return match.Raw;
                }

                // 2. Words from Product.Name
                if (!string.IsNullOrEmpty(product.Name))
                {
                    var words = product.Name
                        .Split(new[] { ' ', '-', '/', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(Normalize)
                        .Where(w => !string.IsNullOrEmpty(w));

                    foreach (var word in words)
                    {
                        if (dictSet.Contains(word))
                            return dict.First(v => v.Normalized == word).Raw;

                        var contains = dict.FirstOrDefault(v =>
                            (" " + v.Normalized + " ").Contains(" " + word + " "));
                        if (contains != null) return contains.Raw;
                    }
                }

                return fallback;
            }

            return !string.IsNullOrEmpty(product.SupplierName)
                ? Normalize(product.SupplierName)
                : fallback;
        }

        private string GetBrandMatchingValue(Product product, CategoryParameter param)
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

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var lowered = value.Trim().ToLowerInvariant();
            return lowered == "rollon-solid" ? "rollon" : lowered;
        }
    }
}