using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using JSAGROAllegroSync.Services.GaskaApi.Interfaces;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.GaskaApiService
{
    public class GaskaApiService : IGaskaApiService
    {
        private readonly GaskaApiCredentials _apiSettings;
        private readonly IProductRepository _productRepo;
        private readonly HttpClient _http;
        private readonly List<int> _categoriesIds;

        public GaskaApiService(IProductRepository productRepo, HttpClient http, GaskaApiCredentials apiSettings, List<int> categoriesIds)
        {
            _productRepo = productRepo;
            _http = http;
            _apiSettings = apiSettings;
            _categoriesIds = categoriesIds;
        }

        public async Task SyncProducts(CancellationToken ct = default)
        {
            HashSet<int> fetchedProductIds = new HashSet<int>();
            bool hasErrors = false;

            foreach (var categoryId in _categoriesIds)
            {
                int page = 1;
                bool hasMore = true;

                while (hasMore)
                {
                    try
                    {
                        var url = $"/products?category={categoryId}&page={page}&perPage={_apiSettings.ProductsPerPage}&lng=pl";
                        Log.Information($"Sending request to {url}.");
                        var response = await _http.GetAsync(url);

                        if (!response.IsSuccessStatusCode)
                        {
                            Log.Error($"API error while fetching page {page} for category {categoryId}: {response.StatusCode}");
                            hasErrors = true;
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        var apiResponse = JsonConvert.DeserializeObject<ProductsResponse>(json);

                        if (apiResponse.Products == null || apiResponse.Products.Count == 0)
                        {
                            hasMore = false;
                            break;
                        }

                        try
                        {
                            await _productRepo.UpsertProducts(apiResponse.Products, fetchedProductIds, ct);
                            Log.Information($"Successfully fetched and updated {apiResponse.Products.Count} products for category {categoryId}.");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"Error while saving products for category {categoryId}");
                            hasErrors = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error while getting products from page {page} for category {categoryId}.");
                        hasErrors = true;
                        break;
                    }
                    finally
                    {
                        page++;
                        await Task.Delay(TimeSpan.FromSeconds(_apiSettings.ProductsInterval));
                    }
                }
            }

            if (hasErrors)
            {
                Log.Warning("Errors occurred during product sync. Archiving skipped to avoid data inconsistency.");
                return;
            }

            //try
            //{
            //    var archivedCount = await _productRepo.ArchiveProductsNotIn(fetchedProductIds, ct);
            //    Log.Information($"Archived {archivedCount} products.");
            //}
            //catch (Exception ex)
            //{
            //    Log.Error(ex, "An error occurred while checking for products to archive.");
            //}
        }

        public async Task SyncProductDetails(CancellationToken ct = default)
        {
            List<Product> productsToUpdate;

            try
            {
                productsToUpdate = await _productRepo.GetProductsForDetailUpdate(_apiSettings.ProductPerDay, ct);
                if (!productsToUpdate.Any()) return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error while getting products to update details from database");
                return;
            }

            foreach (var product in productsToUpdate)
            {
                try
                {
                    var response = await _http.GetAsync($"/product?id={product.Id}&lng=pl");

                    if (!response.IsSuccessStatusCode)
                    {
                        Log.Error($"API error while fetching product details. Product name: {product.Name}. Product Code: {product.CodeGaska}. Response Status: {response.StatusCode}");
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var apiResponse = JsonConvert.DeserializeObject<ProductResponse>(json);

                    if (apiResponse?.Product == null) continue;

                    await _productRepo.UpdateProductDetails(product, apiResponse.Product, ct);
                    Log.Information($"Successfully fetched and updated details of product {product.Name} ({product.CodeGaska})");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error while updating product details. Product name: {product.Name} Code: {product.CodeGaska}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(_apiSettings.ProductInterval));
                }
            }
        }
    }
}