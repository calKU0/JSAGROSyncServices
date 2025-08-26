using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Repositories.Interfaces;
using JSAGROAllegroSync.Services.AllegroApi.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi
{
    public class AllegroCompatibilityService : IAllegroCompatibilityService
    {
        private readonly IProductRepository _productRepo;
        private readonly AllegroApiClient _apiClient;

        public AllegroCompatibilityService(IProductRepository productRepo, AllegroApiClient apiClient)
        {
            _productRepo = productRepo;
            _apiClient = apiClient;
        }

        public async Task FetchAndSaveCompatibleProducts(CancellationToken ct = default)
        {
            try
            {
                var types = new[] { "TRACTOR" };

                foreach (var type in types)
                {
                    var groups = await _apiClient.GetAsync<CompatibleProductGroupsResponse>($"/sale/compatible-products/groups?type={type}&limit=200&offset=0", ct);
                    if (groups?.Groups == null || !groups.Groups.Any())
                        continue;

                    foreach (var group in groups.Groups)
                    {
                        try
                        {
                            var products = await FetchCompatibilityList(type, group.Id, ct);

                            var productEntities = products.Select(p => new CompatibleProduct
                            {
                                Id = p.Id,
                                Name = p.Attributes
                                    .Where(a => a.Id == "MODEL")
                                    .Select(a => string.Join(",", a.Values))
                                    .FirstOrDefault(),
                                Type = type,
                                GroupName = group.Text
                            }).ToList();

                            await _productRepo.SaveCompatibleProductsAsync(productEntities, ct);
                            Log.Information("Saved {Count} compatible machines for type {Type}", productEntities.Count, type);
                        }
                        catch (Exception exGroup)
                        {
                            Log.Error(exGroup, "Error processing compatible machines for type {Type}", type);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while fetching and saving compatible products.");
            }
        }

        private async Task<List<CompatibleProductDto>> FetchCompatibilityList(string type, string groupId, CancellationToken ct)
        {
            var allProducts = new List<CompatibleProductDto>();
            int limit = 200;
            int offset = 0;

            while (true)
            {
                try
                {
                    var url = $"/sale/compatible-products?type={type}&group.id={groupId}&limit={limit}&offset={offset}";
                    var response = await _apiClient.GetAsync<CompatibleProductsResponse>(url, ct);

                    if (response?.CompatibleProducts == null || !response.CompatibleProducts.Any())
                        break;

                    allProducts.AddRange(response.CompatibleProducts);
                    offset += limit;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch compatibility list for type {Type}", type, groupId, offset);
                    break;
                }
            }

            return allProducts;
        }
    }
}