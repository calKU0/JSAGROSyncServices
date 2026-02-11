using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Services.Allegro
{
    public class AllegroCategoryService : IAllegroCategoryService
    {
        private ILogger<AllegroCategoryService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly ICategoryRepository _categoryRepo;
        private readonly AllegroApiClient _apiClient;

        public AllegroCategoryService(IProductRepository productRepo, AllegroApiClient apiClient, ILogger<AllegroCategoryService> logger, ICategoryRepository categoryRepo)
        {
            _productRepo = productRepo;
            _apiClient = apiClient;
            _logger = logger;
            _categoryRepo = categoryRepo;
        }

        public async Task UpdateAllegroCategories(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsWithoutDefaultCategory(ct);
                if (!products.Any()) return;

                var categoryResults = new List<(int ProductId, int CategoryId, string CodeGaska, string Name)>();

                foreach (var product in products)
                {
                    try
                    {
                        int categoryId = await GetCategoriesSuggestions(product, ct);
                        categoryResults.Add((product.Id, categoryId, product.Code, product.Name));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while fetching suggested Allegro category for {Name} ({Code})", product.Name, product.Code);
                    }
                }

                foreach (var result in categoryResults)
                {
                    try
                    {
                        await _productRepo.UpdateProductAllegroCategory(result.ProductId, result.CategoryId, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error updating Allegro category in DB for {Name} ({Code})", result.Name, result.CodeGaska);
                    }
                }

                // Run DB-based fallback
                //products = await _productRepo.GetProductsWithoutDefaultCategory(ct);
                //foreach (var product in products)
                //{
                //    try
                //    {
                //        var dbCategory = await _categoryRepo.GetMostCommonDefaultAllegroCategory(product.Id, ct);
                //        if (dbCategory.HasValue)
                //        {
                //            await _productRepo.UpdateProductAllegroCategory(product.Id, dbCategory.Value, ct);
                //        }
                //        else
                //        {
                //            await _productRepo.UpdateProductAllegroCategory(product.Id, 319159, ct);
                //        }
                //    }
                //    catch (Exception ex)
                //    {
                //        _logger.LogError(ex, "Error updating DB-based category for {Name} ({Code})", product.Name, product.Code);
                //    }
                //}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in categories mapping.");
            }
        }

        private async Task<int> GetCategoriesSuggestions(RolmarProduct product, CancellationToken ct)
        {
            try
            {
                var result = await _apiClient.GetAsync<MatchingCategoriesResponse>($"/sale/matching-categories?name={product.Name}", ct);

                var categories = result?.MatchingCategories;
                if (categories == null || !categories.Any()) return 0;

                var selectedCategory = categories.First();

                await _categoryRepo.SaveCategoryTreeAsync(selectedCategory, ct);

                _logger.LogInformation("Selected category for {CodeGaska}: {Path}", product.Code, BuildCategoryPath(selectedCategory, c => c.Name));

                return Convert.ToInt32(selectedCategory.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in category suggestion for product {CodeGaska}", product.Code);
                return 0;
            }
        }

        private string BuildCategoryPath(CategoryDto category, Func<CategoryDto, string> selector)
        {
            var stack = new Stack<string>();
            var current = category;
            while (current != null)
            {
                stack.Push(selector(current));
                current = current.Parent;
            }
            return string.Join("/", stack);
        }

        private string BuildCategoryIdPath(CategoryDto category)
        {
            var stack = new Stack<string>();
            var current = category;
            while (current != null)
            {
                stack.Push(current.Id);
                current = current.Parent;
            }
            return string.Join("/", stack);
        }

        public async Task FetchAndSaveCategoryParameters(CancellationToken ct = default)
        {
            try
            {
                var categories = await _categoryRepo.GetDefaultCategories(ct);
                var allCategoryParameters = new List<CategoryParameter>();

                foreach (var category in categories)
                {
                    try
                    {
                        var result = await _apiClient.GetAsync<CategoryParametersResponse>(
                            $"/sale/categories/{category}/parameters", ct);

                        if (result?.Parameters == null || !result.Parameters.Any())
                        {
                            _logger.LogWarning("No parameters returned for category {Category}", category);
                            continue;
                        }

                        var categoryParameters = result.Parameters.Select(p => new CategoryParameter
                        {
                            ParameterId = Convert.ToInt32(p.Id),
                            CategoryId = category,
                            Name = p.Name,
                            Type = p.Type,
                            Required = p.Required,
                            RequiredForProduct = p.RequiredForProduct,
                            Min = p.Restrictions?.GetMin(),
                            Max = p.Restrictions?.GetMax(),
                            AmbiguousValueId = p.Options.AmbiguousValueId,
                            CustomValuesEnabled = p.Options.CustomValuesEnabled,
                            DescribesProduct = p.Options.DescribesProduct,
                            Values = p.Dictionary?.Select(d => new CategoryParameterValue
                            {
                                Value = d.Value
                            }).ToList() ?? new List<CategoryParameterValue>()
                        }).ToList();

                        allCategoryParameters.AddRange(categoryParameters);
                        _logger.LogInformation("Fetched {Count} parameters for category {Category}.", categoryParameters.Count, category);
                    }
                    catch (Exception exCategory)
                    {
                        _logger.LogError(exCategory, "Error fetching parameters for category {Category}", category);
                    }
                }

                if (allCategoryParameters.Any())
                {
                    // Bulk save all categories at once
                    await _categoryRepo.SaveCategoryParametersAsync(allCategoryParameters, ct);
                    _logger.LogInformation("Saved total {Count} parameters for {CategoryCount} categories",
                        allCategoryParameters.Count, categories.Count());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in FetchAndSaveCategoryParameters.");
            }
        }
    }
}