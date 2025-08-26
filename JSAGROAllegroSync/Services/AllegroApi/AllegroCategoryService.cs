using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories;
using JSAGROAllegroSync.Services.AllegroApi.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi
{
    public class AllegroCategoryService : IAllegroCategoryService
    {
        private readonly ProductRepository _productRepo;
        private readonly CategoryRepository _categoryRepo;
        private readonly AllegroApiClient _apiClient;

        public AllegroCategoryService(ProductRepository productRepo, CategoryRepository categoryRepo, AllegroApiClient apiClient)
        {
            _productRepo = productRepo;
            _categoryRepo = categoryRepo;
            _apiClient = apiClient;
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
                        categoryResults.Add((product.Id, categoryId, product.CodeGaska, product.Name));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error while fetching suggested Allegro category for {Name} ({Code})", product.Name, product.CodeGaska);
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
                        Log.Error(ex, "Error updating Allegro category in DB for {Name} ({Code})", result.Name, result.CodeGaska);
                    }
                }

                // Run DB-based fallback
                products = await _productRepo.GetProductsWithoutDefaultCategory(ct);
                foreach (var product in products)
                {
                    try
                    {
                        var dbCategory = await _categoryRepo.GetMostCommonDefaultAllegroCategory(product.Id, ct);
                        if (dbCategory.HasValue)
                        {
                            await _productRepo.UpdateProductAllegroCategory(product.Id, dbCategory.Value, ct);
                        }
                        else
                        {
                            Log.Warning("No Allegro category resolved for product {Name} ({Code})", product.Name, product.CodeGaska);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error updating DB-based category for {Name} ({Code})", product.Name, product.CodeGaska);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in categories mapping.");
            }
        }

        private async Task<int> GetCategoriesSuggestions(Product product, CancellationToken ct)
        {
            try
            {
                string nextPreferredChildId = "252202"; // Default
                if (product.Applications?.Any(a => a.ParentID != 0) == true)
                {
                    if (product.Applications.Any(a =>
                        a.Name?.IndexOf("kombajn", StringComparison.OrdinalIgnoreCase) >= 0))
                        nextPreferredChildId = "319108";
                    else if (product.Applications.Any(a =>
                        a.Name?.IndexOf("Ciągnik", StringComparison.OrdinalIgnoreCase) >= 0))
                        nextPreferredChildId = "252204";
                }

                var result = await _apiClient.GetAsync<MatchingCategoriesResponse>($"/sale/matching-categories?name={product.Name}", ct);

                var categories = result?.MatchingCategories;
                if (categories == null || !categories.Any()) return 0;

                const string preferredRootId = "99022";
                const string preferredRootChildId = "252182";

                var candidatesUnderRoot = categories
                    .Where(c => BuildCategoryIdPath(c).Split('/').Contains(preferredRootId))
                    .ToList();
                if (!candidatesUnderRoot.Any()) return 0;

                var candidatesUnderRootChild = candidatesUnderRoot
                    .Where(c => BuildCategoryIdPath(c).Split('/').Contains(preferredRootChildId))
                    .ToList();
                if (!candidatesUnderRootChild.Any()) return 0;

                var selectedCategory = candidatesUnderRootChild.FirstOrDefault(c => BuildCategoryIdPath(c).Split('/').Contains(nextPreferredChildId));

                if (selectedCategory == null && nextPreferredChildId == "319108")
                {
                    const string secondaryFallbackId = "319159";
                    selectedCategory = candidatesUnderRootChild.FirstOrDefault(c => BuildCategoryIdPath(c).Split('/').Contains(secondaryFallbackId));
                }

                if (selectedCategory == null)
                {
                    selectedCategory = candidatesUnderRootChild.First();
                }

                await _categoryRepo.SaveCategoryTreeAsync(selectedCategory, ct);

                Log.Information("Selected category for {CodeGaska}: {Path}", product.CodeGaska, BuildCategoryPath(selectedCategory, c => c.Name));

                return Convert.ToInt32(selectedCategory.Id);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception in category suggestion for product {CodeGaska}", product.CodeGaska);
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

                foreach (var category in categories)
                {
                    try
                    {
                        var result = await _apiClient.GetAsync<CategoryParametersResponse>($"/sale/categories/{category}/parameters", ct);

                        if (result?.Parameters == null || !result.Parameters.Any())
                        {
                            Log.Warning("No parameters returned for category {Category}", category);
                            continue;
                        }

                        var categoryResult = result.Parameters.Select(p => new CategoryParameter
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
                            }).ToList()
                        }).ToList();

                        await _categoryRepo.SaveCategoryParametersAsync(categoryResult, ct);

                        Log.Information("Saved {Count} parameters for category {Category}", categoryResult.Count, category);
                    }
                    catch (Exception exCategory)
                    {
                        Log.Error(exCategory, "Error fetching parameters for category {Category}", category);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error in FetchAndSaveCategoryParameters.");
            }
        }
    }
}