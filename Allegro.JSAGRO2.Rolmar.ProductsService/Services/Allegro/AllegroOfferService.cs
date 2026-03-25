using Allegro.JSAGRO2.Rolmar.ProductsService.Constants;
using Allegro.JSAGRO2.Rolmar.ProductsService.Helpers;
using Allegro.JSAGRO2.Rolmar.ProductsService.Settings;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Helpers;
using JSAGROSyncServices.Infrastructure.Services;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Allegro.JSAGRO2.Rolmar.ProductsService.Services.Allegro
{
    public class AllegroOfferService : IAllegroOfferService
    {
        private readonly ILogger<AllegroOfferService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly IOfferRepository _offerRepo;
        private readonly IParameterRepository _parameterRepo;
        private readonly IImageRepository _imageRepo;
        private readonly AllegroApiClient _apiClient;
        private readonly AppSettings _appSettings;
        private readonly PriceSettings _priceSettings;
        private readonly AllegroSettings _allegroSettings;

        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.Preserve,
            WriteIndented = true
        };

        public AllegroOfferService(IProductRepository productRepo, IOfferRepository offerRepo, AllegroApiClient apiClient, IOptions<AppSettings> appsettings, IOptions<PriceSettings> priceSettings, IOptions<AllegroSettings> allegroSettings, ILogger<AllegroOfferService> logger, IParameterRepository parameterRepo, IImageRepository imageRepo)
        {
            _productRepo = productRepo;
            _offerRepo = offerRepo;
            _apiClient = apiClient;
            _appSettings = appsettings.Value;
            _priceSettings = priceSettings.Value;
            _allegroSettings = allegroSettings.Value;
            _logger = logger;
            _parameterRepo = parameterRepo;
            _imageRepo = imageRepo;
        }

        public async Task SyncAllegroOffers(CancellationToken ct = default)
        {
            try
            {
                var allOffers = await FetchAllOffers(ct);

                var shippingRates = await _apiClient.GetAsync<AllegroShippingRatesResponse>("/sale/shipping-rates", ct);
                var shippingDict = shippingRates?.ShippingRates?.ToDictionary(s => s.Id, s => s.Name) ?? new Dictionary<string, string>();

                // Split offers into two sets
                var offersWithExternalId = allOffers.Where(o => !string.IsNullOrEmpty(o?.External?.Id)).ToList();
                var offersWithoutExternalId = allOffers.Where(o => string.IsNullOrEmpty(o?.External?.Id)).ToList();

                // Group by External.Id when present
                var latestOffers = offersWithExternalId
                    .GroupBy(o => o.External.Id)
                    .Select(g => g.OrderByDescending(o => o.Id).First())
                    .ToList();

                // Optionally group by Name for those without External.Id
                var groupedByName = offersWithoutExternalId
                    .Where(o => !string.IsNullOrWhiteSpace(o.Name))
                    .GroupBy(o => o.Name)
                    .Select(g => g.OrderByDescending(o => o.Id).First())
                    .ToList();

                // Merge both lists
                latestOffers.AddRange(groupedByName);

                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = 25
                };

                // Update shipping info & categories
                foreach (var offer in latestOffers)
                {
                    if (offer.Delivery?.ShippingRates?.Id != null &&
                        shippingDict.TryGetValue(offer.Delivery.ShippingRates.Id, out var name))
                    {
                        offer.Delivery.ShippingRates.Name = name;
                    }

                    //if (offer.External?.Id != null && offer.Publication.Status != "ENDED")
                    //{
                    //    await _productRepo.UpdateProductAllegroCategory(offer.External.Id, offer.Category.Id, ct);
                    //}
                }

                _logger.LogInformation("Attempting to update database offers.");
                await _offerRepo.UpsertOffers(latestOffers, ct);
                _logger.LogInformation("Fetched and saved {Count} offers from Allegro.", latestOffers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while fetching and saving offers.");
                throw;
            }
        }

        public async Task SyncAllegroOffersDetails(CancellationToken ct = default)
        {
            try
            {
                var allOffers = await _offerRepo.GetOffersWithoutDetails(ct);

                var offersDetails = new ConcurrentBag<AllegroOfferDetails.Root>();
                int processedCount = 0;

                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = 25
                };

                await Parallel.ForEachAsync(allOffers, parallelOptions, async (offer, token) =>
                {
                    try
                    {
                        var detailedOffer = await _apiClient.GetAsync<AllegroOfferDetails.Root>(
                            $"/sale/product-offers/{offer.Id}", token);

                        if (detailedOffer == null)
                            return;

                        detailedOffer.Delivery.ShippingRates.Id = offer.DeliveryName;

                        offersDetails.Add(detailedOffer);

                        var current = Interlocked.Increment(ref processedCount);

                        if (current % 500 == 0)
                        {
                            _logger.LogInformation("Processed {ProcessedCount} / {TotalCount} offers. Details collected so far: {DetailsCount}", current, allOffers.Count, offersDetails.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        var current = Interlocked.Increment(ref processedCount);

                        _logger.LogError(ex, "Exception while fetching details for offer ID {OfferId}. Processed so far: {ProcessedCount}", offer.Id, current);
                    }
                });

                if (!offersDetails.IsEmpty)
                {
                    await _offerRepo.UpsertOfferDetails(offersDetails.ToList(), ct);
                }

                _logger.LogInformation("Finished syncing Allegro offer details. Processed {ProcessedCount} offers. Saved {SavedCount} details.", processedCount, offersDetails.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while fetching and saving offers.");
            }
        }

        private async Task<List<Offer>> FetchAllOffers(CancellationToken ct)
        {
            const int limit = 100;
            const int maxParallelism = 25;

            var allOffers = new ConcurrentBag<Offer>();

            try
            {
                var firstPage = await _apiClient.GetAsync<OffersResponse>($"/sale/offers?limit={limit}&offset=0", ct);

                if (firstPage?.Offers == null || firstPage.Offers.Count == 0)
                {
                    _logger.LogInformation("No offers found.");
                    return new List<Offer>();
                }

                foreach (var offer in firstPage.Offers)
                    allOffers.Add(offer);

                int totalCount = firstPage.TotalCount;
                int totalPages = (int)Math.Ceiling((double)totalCount / limit);

                _logger.LogInformation("Fetched page 1 with {PageCount} offers. Total offers reported: {TotalCount}. Total pages: {TotalPages}", firstPage.Offers.Count, totalCount, totalPages);

                var offsets = Enumerable.Range(1, totalPages - 1)
                    .Select(page => page * limit)
                    .ToList();

                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = maxParallelism
                };

                int processedPages = 1;

                await Parallel.ForEachAsync(offsets, parallelOptions, async (offset, token) =>
                {
                    int pageNumber = (offset / limit) + 1;

                    try
                    {
                        var page = await _apiClient.GetAsync<OffersResponse>(
                            $"/sale/offers?limit={limit}&offset={offset}", token);

                        if (page?.Offers == null)
                            return;

                        foreach (var offer in page.Offers)
                            allOffers.Add(offer);

                        var currentPage = Interlocked.Increment(ref processedPages);

                        _logger.LogInformation("Fetched page {PageNumber} with {PageCount} offers. Progress: {ProcessedPages}/{TotalPages}. Total collected: {TotalCollected}", pageNumber, page.Offers.Count, currentPage, totalPages, allOffers.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception while fetching page {PageNumber}", pageNumber);
                    }
                });

                _logger.LogInformation("Finished fetching offers. Total fetched: {TotalCount}", allOffers.Count);

                return allOffers.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while fetching offers.");
                throw;
            }
        }

        public async Task UpdateOffers(CancellationToken ct = default)
        {
            try
            {
                var offers = await _offerRepo.GetOffersToUpdate(ct);

                // Limit concurrency to avoid rate-limits (tune this number)
                var parallelOptions = new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = 25
                };

                await Parallel.ForEachAsync(offers, parallelOptions, async (offer, token) =>
                {
                    try
                    {
                        // 🔹 Import images if missing
                        if (offer.Product.AllegroImages == null || !offer.Product.AllegroImages.Any())
                        {
                            _logger.LogInformation(
                                "No images for product {Name} ({Code}), importing...",
                                offer.Product.Name,
                                offer.Product.Code);

                            var images = await ImportImages(offer.Product, token);

                            if (images == null || !images.Any())
                            {
                                _logger.LogWarning(
                                    "Skipping offer update for {Name} ({Code}) due to no images.",
                                    offer.Product.Name,
                                    offer.Product.Code);
                                return;
                            }

                            offer.Product.AllegroImages = images;
                        }

                        var offerDto = OfferFactory.PatchOffer(offer, _appSettings, _allegroSettings, _priceSettings);

                        var response = await _apiClient.SendWithResponseAsync(
                            $"/sale/product-offers/{offer.Id}",
                            HttpMethod.Patch,
                            offerDto,
                            token);

                        var body = await response.Content.ReadAsStringAsync(token);

                        await LogAllegroResponse(offer.Product, response, body, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Exception while updating offer for {Name} ({Code})",
                            offer.Product.Name,
                            offer.Product.Code);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while updating Allegro offers.");
            }
        }

        public async Task CreateOffers(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsToUpload(_appSettings.MinProductStock, _appSettings.MinProductPrice, ct);

                if (products == null || !products.Any())
                {
                    _logger.LogInformation("No products to upload.");
                    return;
                }

                await Parallel.ForEachAsync(products, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = 25
                },
                async (product, token) =>
                {
                    try
                    {
                        product.AllegroImages = await ImportImages(product, token);
                        if (product.AllegroImages == null || !product.AllegroImages.Any())
                        {
                            _logger.LogWarning("Skipping product {Name} ({Code}) due to no images.", product.Name, product.Code);
                            return;
                        }
                        var offer = OfferFactory.BuildOffer(product, _appSettings, _allegroSettings, _priceSettings);
                        var response = await _apiClient.SendWithResponseAsync("/sale/product-offers", HttpMethod.Post, offer, token);
                        var body = await response.Content.ReadAsStringAsync();
                        await LogAllegroResponse(product, response, body);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception while creating offer for {Name} ({Code})", product.Name, product.Code);
                    }
                });

                _logger.LogInformation("Finished creating Allegro offers.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while creating Allegro offers.");
            }
        }

        private async Task LogAllegroResponse(RolmarProduct product, HttpResponseMessage response, string body, bool isUpdate = false)
        {
            var action = isUpdate ? "updated" : "created";

            switch ((int)response.StatusCode)
            {
                case 200:
                    _logger.LogInformation($"Offer {action} successfully for {product.Name} ({product.Code})");
                    await _imageRepo.MarkImagesAsConnectedAsync(product.Id, new CancellationToken());
                    break;

                case 201:
                    _logger.LogInformation($"Offer {action} successfully for {product.Name} ({product.Code})");
                    await _imageRepo.MarkImagesAsConnectedAsync(product.Id, new CancellationToken());
                    break;

                case 202:
                    _logger.LogInformation($"Offer {action} successfully but still processing for {product.Name} ({product.Code})");
                    await _imageRepo.MarkImagesAsConnectedAsync(product.Id, new CancellationToken());
                    break;

                case 400:
                case 422:
                case 433:
                    await _imageRepo.DeleteNotConnectedImages(product.Id, CancellationToken.None);
                    await LogAllegroErrors(product, response, body, isUpdate);
                    break;

                case 401:
                    await _imageRepo.DeleteNotConnectedImages(product.Id, CancellationToken.None);
                    _logger.LogError($"Unauthorized (401). Check token for product {product.Code} when {action} offer.");
                    break;

                case 403:
                    await _imageRepo.DeleteNotConnectedImages(product.Id, CancellationToken.None);
                    _logger.LogError($"Forbidden (403). No permission for {action} offer for {product.Code}.");
                    break;

                case 404:
                    _logger.LogWarning("Offer not found in Allegro. Deleting from database.");
                    await _offerRepo.DeleteOffer(product.Id, CancellationToken.None);
                    break;

                default:
                    await _imageRepo.DeleteNotConnectedImages(product.Id, CancellationToken.None);
                    _logger.LogError($"Unexpected status {(int)response.StatusCode} ({response.StatusCode}) while {action} offer for {product.Code}. Response: {body}");
                    break;
            }
        }

        private async Task LogAllegroErrors(RolmarProduct product, HttpResponseMessage response, string body, bool isUpdate = false)
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
                        if (((err.Code?.Contains("ProductConstraintViolationException", StringComparison.OrdinalIgnoreCase) == true && (err.UserMessage ?? "").Contains("kategorii produktu", StringComparison.OrdinalIgnoreCase)) || err.Code?.Contains("CATEGORY_MISMATCH", StringComparison.OrdinalIgnoreCase) == true) && !string.IsNullOrEmpty(err.UserMessage))
                        {
                            var correctCategoryId = ExtractCorrectCategoryId(err.UserMessage);
                            if (!string.IsNullOrEmpty(correctCategoryId))
                            {
                                await _productRepo.UpdateProductAllegroCategory(product.Id, Convert.ToInt32(correctCategoryId), CancellationToken.None);
                                _logger.LogInformation("Updated category for {Name} ({Code}) to {CategoryId}", product.Name, product.Code, correctCategoryId);
                            }
                        }
                        else if (
                            (err.Code == "PARAMETER_MISMATCH" && !string.IsNullOrEmpty(err.UserMessage)) ||
                            (err.Code == "ProductConstraintViolationException.DataIntegrity" &&
                             err.Message.Contains("Incorrect value of the") &&
                             err.Message.Contains("parameter for the offered product"))
                        )
                        {
                            _logger.LogInformation("Offer {Action} error for {Name}: Code={Code}, Message={Message}, UserMessage={UserMessage}, Path={Path}, Details={Details}", action, product.Name, err.Code, err.Message, err.UserMessage ?? "N/A", err.Path ?? "N/A", err.Details ?? "N/A");
                            // Try to extract from either UserMessage or Message
                            var sourceMessage = !string.IsNullOrEmpty(err.UserMessage) ? err.UserMessage : err.Message;

                            var correctValue = ExtractCorrectParameterValue(sourceMessage);
                            var parameterId = ExtractParameterIdFromConstraintMessage(err.Message);

                            if (!string.IsNullOrEmpty(parameterId) && !string.IsNullOrEmpty(correctValue))
                            {
                                await _parameterRepo.UpdateParameter(product.Id, Convert.ToInt32(parameterId), correctValue, CancellationToken.None);
                                _logger.LogInformation("Updated parameter {ParameterId} for {Name} ({Code}) to '{CorrectValue}'", parameterId, product.Name, product.Code, correctValue);
                            }
                        }
                        else if (err.UserMessage.Contains(@"Podany adres obrazka jest nieprawidłowy."))
                        {
                            await _imageRepo.DeleteProductImagesAsync(product.Id, CancellationToken.None);
                        }
                        else if (err.UserMessage.Contains(@"bez wybierania wartości niejednoznacznej"))
                        {
                        }
                        else if (err.Code == "OfferNotFoundException" && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            _logger.LogWarning("Offer not found in Allegro. Deleting from database.");
                            await _offerRepo.DeleteOffer(product.Id, CancellationToken.None);
                        }
                        else
                        {
                            _logger.LogError("Offer {Action} error for {ProductCode}: Code={Code}, Message={Message}, UserMessage={UserMessage}, Path={Path}, Details={Details}",
                                action, product.Code, err.Code, err.Message, err.UserMessage ?? "N/A", err.Path ?? "N/A", err.Details ?? "N/A");
                        }
                    }
                }
                else
                {
                    _logger.LogError($"Offer {action} error {response.StatusCode} for {product.Name}: {body}");
                }
            }
            catch (Exception exParse)
            {
                _logger.LogError(exParse, $"Failed to parse Allegro error ({response.StatusCode}) while {action} offer for {product.Name}. Body={body}");
            }
        }

        private string ExtractCorrectCategoryId(string message)
        {
            // Try to match specifically "produktu (123456)" first (preferred pattern)
            var correctMatch = Regex.Match(message, @"produktu\s*\((\d+)\)", RegexOptions.IgnoreCase);
            if (correctMatch.Success)
                return correctMatch.Groups[1].Value;

            // Fallback: if message contains multiple category IDs, assume the last one is correct
            var allMatches = Regex.Matches(message, @"\((\d+)\)");
            if (allMatches.Count > 1)
                return allMatches[^1].Groups[1].Value;

            return allMatches.Count == 1 ? allMatches[0].Groups[1].Value : null;
        }

        private string ExtractCorrectParameterValue(string message)
        {
            // Handles: "change the value to `JAG`" OR `"JAG"` OR similar phrases
            var match = Regex.Match(message, @"value\s*(?:to|is)\s*[`""]([^`""]+)[`""]", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractParameterIdFromConstraintMessage(string message)
        {
            var match = Regex.Match(message, @"id:\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private async Task<List<AllegroImages>> ImportImages(RolmarProduct product, CancellationToken ct)
        {
            var imageResults = new ConcurrentBag<(string FileName, string Url)>();

            if (!Directory.Exists(ServiceConstants.ImagesFolder))
            {
                _logger.LogWarning("Images folder not found: {Path}", ServiceConstants.ImagesFolder);
                return new List<AllegroImages>();
            }

            var imageFiles = ImageHelper.GetImageFiles(ServiceConstants.ImagesFolder, product.Id);

            if (!imageFiles.Any())
            {
                return new List<AllegroImages>();
            }

            // Upload product images in parallel
            await Parallel.ForEachAsync(
                imageFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 3,
                    CancellationToken = ct
                },
                async (filePath, token) =>
                {
                    try
                    {
                        var imageBytes = await File.ReadAllBytesAsync(filePath, token);
                        var validatedBytes = Utils.EnsureImageMinSize(imageBytes);

                        if (validatedBytes == null)
                        {
                            _logger.LogWarning("Image too small or invalid: {File}", filePath);
                            return;
                        }

                        var contentType = Utils.GetContentTypeFromPath(filePath);

                        var uploadResult = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", validatedBytes, token, contentType);

                        if (!string.IsNullOrWhiteSpace(uploadResult?.Location))
                        {
                            imageResults.Add((Path.GetFileName(filePath), uploadResult.Location));

                            _logger.LogInformation("Uploaded image {File} -> {Url}", Path.GetFileName(filePath), uploadResult.Location);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error uploading image {File}", Path.GetFileName(filePath));
                    }
                });

            // Sort uploaded product images alphabetically
            var orderedUrls = imageResults
                .OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Url)
                .ToList();

            // Upload logo LAST
            var logoPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "Images",
                "jsagro-logo.jpg");

            try
            {
                if (File.Exists(logoPath))
                {
                    var logoBytes = await File.ReadAllBytesAsync(logoPath, ct);
                    var logoResult = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", logoBytes, ct, "image/jpeg");

                    if (!string.IsNullOrWhiteSpace(logoResult?.Location))
                    {
                        orderedUrls.Add(logoResult.Location);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upload logo image.");
            }

            _logger.LogInformation("Imported {Count} images for product {Code}", orderedUrls.Count, product.Code);

            var result = new List<AllegroImages>(orderedUrls.Count);

            foreach (var url in orderedUrls)
            {
                var imageId = await _imageRepo.AddImageAsync(product.Id, url, ct);

                result.Add(new AllegroImages
                {
                    Id = imageId,
                    ProductId = product.Id,
                    Url = url,
                    Connected = false
                });
            }

            return result;
        }
    }
}