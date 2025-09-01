using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.Settings;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using JSAGROAllegroSync.Services.AllegroApi.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi
{
    public class AllegroOfferService : IAllegroOfferService
    {
        private readonly IProductRepository _productRepo;
        private readonly IOfferRepository _offerRepo;
        private readonly ICategoryRepository _categoryRepo;
        private readonly AllegroApiClient _apiClient;
        private readonly AppSettings _appSettings = AppSettingsLoader.LoadAppSettings();

        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        public AllegroOfferService(IProductRepository productRepo, IOfferRepository offerRepo, ICategoryRepository categoryRepo, AllegroApiClient apiClient)
        {
            _productRepo = productRepo;
            _offerRepo = offerRepo;
            _categoryRepo = categoryRepo;
            _apiClient = apiClient;
        }

        public async Task SyncAllegroOffers(CancellationToken ct = default)
        {
            try
            {
                var allOffers = await FetchAllOffers(ct);

                var shippingRates = await _apiClient.GetAsync<ShippingRatesReponse>("/sale/shipping-rates", ct);
                var shippingDict = shippingRates?.ShippingRates?.ToDictionary(s => s.Id, s => s.Name) ?? new Dictionary<string, string>();
                var latestOffers = allOffers
                    .Where(o => o?.External?.Id != null)
                    .GroupBy(o => o.External.Id)
                    .Select(g => g.OrderByDescending(o => o.Id).FirstOrDefault())
                    .ToList();

                foreach (var offer in latestOffers)
                {
                    if (offer.Delivery?.ShippingRates?.Id != null && shippingDict.TryGetValue(offer.Delivery.ShippingRates.Id, out var name))
                    {
                        offer.Delivery.ShippingRates.Name = name;
                    }

                    await _productRepo.UpdateProductAllegroCategory(offer.External.Id, offer.Category.Id, ct);
                }

                await _offerRepo.UpsertOffers(latestOffers, ct);
                Log.Information("Fetched and saved {Count} offers from Allegro.", latestOffers.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while fetching and saving offers.");
            }
        }

        private async Task<List<Offer>> FetchAllOffers(CancellationToken ct)
        {
            var allOffers = new List<Offer>();
            int limit = 1000;
            int offset = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var page = await _apiClient.GetAsync<OffersResponse>($"/sale/offers?limit={limit}&offset={offset}", ct);

                    if (page?.Offers == null || !page.Offers.Any()) break;

                    allOffers.AddRange(page.Offers);
                    if (page.Offers.Count < limit) break;

                    offset += limit;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Exception while fetching offers at offset {Offset}", offset);
                    break;
                }
            }

            return allOffers;
        }

        public async Task UpdateOffers(CancellationToken ct = default)
        {
            try
            {
                var offers = await _offerRepo.GetOffersToUpdate(ct);
                var compatibleProducts = await _categoryRepo.GetCompatibilityList(ct);
                var allegroCategories = await _categoryRepo.GetAllegroCategories(ct);

                foreach (var offer in offers)
                {
                    try
                    {
                        var offerDto = OfferFactory.PatchOffer(offer, compatibleProducts, allegroCategories, _appSettings);
                        var response = await _apiClient.SendWithResponseAsync($"/sale/product-offers/{offer.Id}", new HttpMethod("PATCH"), offerDto, ct);
                        var body = await response.Content.ReadAsStringAsync();
                        await LogAllegroResponse(offer.Product, response, body, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Exception while updating offer for {Name} ({Code})", offer.Product.Name, offer.Product.CodeGaska);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while updating Allegro offers.");
            }
        }

        public async Task CreateOffers(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsToUpload(ct);
                var compatibleProducts = await _categoryRepo.GetCompatibilityList(ct);
                var allegroCategories = await _categoryRepo.GetAllegroCategories(ct);

                foreach (var product in products)
                {
                    try
                    {
                        var offer = OfferFactory.BuildOffer(product, compatibleProducts, allegroCategories, _appSettings);
                        var response = await _apiClient.SendWithResponseAsync("/sale/product-offers", HttpMethod.Post, offer, ct);
                        var body = await response.Content.ReadAsStringAsync();
                        await LogAllegroResponse(product, response, body);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Exception while creating offer for {Name} ({Code})", product.Name, product.CodeGaska);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while creating Allegro offers.");
            }
        }

        private async Task LogAllegroResponse(Product product, HttpResponseMessage response, string body, bool isUpdate = false)
        {
            var action = isUpdate ? "updated" : "created";

            switch ((int)response.StatusCode)
            {
                case 200:
                    Log.Information($"Offer {action} successfully for {product.Name} ({product.CodeGaska})");
                    break;

                case 201:
                    Log.Information($"Offer {action} successfully for {product.Name} ({product.CodeGaska})");
                    break;

                case 202:
                    Log.Information($"Offer {action} successfully but still processing for {product.Name} ({product.CodeGaska})");
                    break;

                case 400:
                case 422:
                case 433:
                    await LogAllegroErrors(product, response, body, isUpdate);
                    break;

                case 401:
                    Log.Error($"Unauthorized (401). Check token for product {product.CodeGaska} when {action} offer.");
                    break;

                case 403:
                    Log.Error($"Forbidden (403). No permission for {action} offer for {product.CodeGaska}.");
                    break;

                default:
                    Log.Error($"Unexpected status {(int)response.StatusCode} ({response.StatusCode}) while {action} offer for {product.CodeGaska}. Response: {body}");
                    break;
            }
        }

        private async Task LogAllegroErrors(Product product, HttpResponseMessage response, string body, bool isUpdate = false)
        {
            var action = isUpdate ? "updating" : "creating";
            try
            {
                var errorResponse = JsonSerializer.Deserialize<AllegroErrorResponse>(body, _options);
                if (errorResponse?.Errors != null)
                {
                    foreach (var err in errorResponse.Errors)
                    {
                        // Special handling for category mismatch
                        if (err.Code == "CATEGORY_MISMATCH" && !string.IsNullOrEmpty(err.UserMessage))
                        {
                            var correctCategoryId = ExtractCorrectCategoryId(err.UserMessage);
                            if (!string.IsNullOrEmpty(correctCategoryId))
                            {
                                await _productRepo.UpdateProductAllegroCategory(product.Id, Convert.ToInt32(correctCategoryId), CancellationToken.None);
                                //Log.Information("Updated category for {Name} ({Code}) to {CategoryId}", product.Name, product.CodeGaska, correctCategoryId);
                            }
                        }
                        else if (err.Code == "PARAMETER_MISMATCH" && !string.IsNullOrEmpty(err.UserMessage))
                        {
                            var correctValue = ExtractParameterValueFromMessage(err.UserMessage);
                            var parameterId = ExtractParameterIdFromMessage(err.Message);

                            if (!string.IsNullOrEmpty(parameterId) && !string.IsNullOrEmpty(correctValue))
                            {
                                await _productRepo.UpdateParameter(product.Id, Convert.ToInt32(parameterId), correctValue, CancellationToken.None);
                                //Log.Information("Updated parameter {ParameterId} for {Name} ({Code}) to '{CorrectValue}'",
                                //    parameterId, product.Name, product.CodeGaska, correctValue);
                            }
                        }
                        else
                        {
                            Log.Error("Offer {Action} error for {Name}: Code={Code}, Message={Message}, UserMessage={UserMessage}, Path={Path}, Details={Details}",
                                action, product.Name, err.Code, err.Message, err.UserMessage ?? "N/A", err.Path ?? "N/A", err.Details ?? "N/A");
                        }
                    }
                }
                else
                {
                    Log.Error($"Offer {action} error {response.StatusCode} for {product.Name}: {body}");
                }
            }
            catch (Exception exParse)
            {
                Log.Error(exParse, $"Failed to parse Allegro error ({response.StatusCode}) while {action} offer for {product.Name}. Body={body}");
            }
        }

        private string ExtractCorrectCategoryId(string message)
        {
            var matches = Regex.Matches(message, @"\((\d+)\)");
            if (matches.Count > 1)
            {
                // Allegro returns: (providedId) ... (correctId)
                return matches[matches.Count - 1].Groups[1].Value;
            }
            return matches.Count == 1 ? matches[0].Groups[1].Value : null;
        }

        private string ExtractParameterIdFromMessage(string message)
        {
            var match = Regex.Match(message, @"id:\s*(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractParameterValueFromMessage(string message)
        {
            // Example: "change the value to `JAG`"
            var match = Regex.Match(message, @"change the value to\s+`([^`]+)`", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}