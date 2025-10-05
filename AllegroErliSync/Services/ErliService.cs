using AllegroErliSync.DTOs;
using AllegroErliSync.Models;
using AllegroErliSync.Repositories;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AllegroErliSync.Services
{
    public class ErliService
    {
        private readonly ErliClient _erliClient;
        private readonly OfferRepository _offerRepository;

        public ErliService(OfferRepository offerRepository, ErliClient erliClient)
        {
            _offerRepository = offerRepository ?? throw new ArgumentNullException(nameof(offerRepository));
            _erliClient = erliClient ?? throw new ArgumentNullException(nameof(erliClient));
        }

        public async Task SyncOffersWithErli()
        {
            Log.Information("Fetching offers from database...");
            var offers = _offerRepository.GetOffersWithDetails().ToList();
            Log.Information("Total offers fetched: {Count}", offers.Count);

            string after = "0"; // Start cursor
            int limit = 100;
            int totalUpdated = 0;

            while (true)
            {
                var requestBody = new
                {
                    pagination = new
                    {
                        sortField = "externalId",
                        after = after,
                        order = "ASC",
                        limit = limit
                    },
                    fields = new[] { "externalId" }
                };

                // API returns a plain array
                var resultItems = await _erliClient.PostAsync<List<ErliProduct>>("products/_search", requestBody);

                if (resultItems == null || resultItems.Count == 0)
                    break;

                // Update matching offers
                foreach (var item in resultItems)
                {
                    var offer = offers.FirstOrDefault(o => o.Id == item.ExternalId);
                    if (offer != null)
                    {
                        offer.ExistsInErli = true;
                        totalUpdated++;
                    }
                }

                // Prepare next page using the last externalId as `after`
                after = resultItems.Last().ExternalId;

                // Stop if fewer items than limit (no more pages)
                if (resultItems.Count < limit)
                    break;
            }

            // Save updates to database
            _offerRepository.UpdateOffersExistsInErli(offers);

            Log.Information("Erli sync finished. Total offers updated: {UpdatedCount}", totalUpdated);
        }

        public async Task CreateProductsInErli()
        {
            try
            {
                Log.Information("Fetching offers for Erli product creation...");
                var offersToCreate = _offerRepository.GetOffersForErliCreation().ToList();
                Log.Information("Total offers to create in Erli: {Count}", offersToCreate.Count);

                foreach (var offer in offersToCreate)
                {
                    await CreateErliProduct(offer);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get offers to create in erli.");
            }
        }

        private async Task CreateErliProduct(Offer offer)
        {
            if (offer == null)
                throw new ArgumentNullException(nameof(offer));

            try
            {
                var categories = _offerRepository.GetAllCategories().ToDictionary(c => c.Id);
                var breadcrumb = BuildBreadcrumb(offer.CategoryId, categories);

                // Map Offer to ErliCreateProductRequest
                var productRequest = new ErliCreateProductRequest
                {
                    Name = offer.Name,
                    Ean = offer.ExternalId,
                    Sku = offer.ExternalId,
                    ExternalCategories = new List<ErliCategory>
                    {
                        new ErliCategory
                        {
                            Source = "allegro",
                            Breadcrumb = breadcrumb
                        }
                    },
                    ExternalReferences = new List<ErliExternalReference>
                    {
                        new ErliExternalReference
                        {
                            Id = offer.Id,
                            Kind = "allegro",
                        }
                    },
                    ExternalAttributes = offer.Attributes?.Select(attr =>
                    {
                        var values = new List<object>();

                        if (!string.IsNullOrEmpty(attr.ValuesJson))
                        {
                            // Deserialize both arrays
                            var valueNames = JsonConvert.DeserializeObject<List<string>>(attr.ValuesJson);
                            var valueIds = string.IsNullOrEmpty(attr.ValuesIdsJson)
                                ? new List<string>()
                                : JsonConvert.DeserializeObject<List<string>>(attr.ValuesIdsJson);

                            if (string.Equals(attr.Type, "dictionary", StringComparison.OrdinalIgnoreCase))
                            {
                                for (int i = 0; i < valueNames.Count; i++)
                                {
                                    var id = i < valueIds.Count ? valueIds[i] : valueNames[i];
                                    values.Add(new { id, name = valueNames[i] });
                                }
                            }
                            else
                            {
                                // For STRING or ENUM types, just send names as strings
                                values.AddRange(valueNames);
                            }
                        }

                        return new ErliAttribute
                        {
                            Id = attr.AttributeId,
                            Source = "allegro",
                            Type = string.IsNullOrEmpty(attr.Type) ? "string" : attr.Type.ToLower() == "float" ? "number" : attr.Type.ToLower(),
                            Values = values
                        };
                    }).ToList() ?? new List<ErliAttribute>(),
                    Price = (int)(offer.Price * 100),
                    Stock = offer.Stock,
                    Status = offer.Status.ToLower(),
                    DispatchTime = new ErliDispatchTime { Period = 3 },
                    Images = string.IsNullOrWhiteSpace(offer.Images)
                        ? new List<ErliImage>()
                        : JsonConvert.DeserializeObject<List<string>>(offer.Images)
                            .Where(url => !string.IsNullOrWhiteSpace(url))
                            .Select(url => new ErliImage { Url = url })
                            .ToList(),
                    Weight = (int)(offer.Weight * 1000),
                    InvoiceType = "vatInvoice"
                };

                int maxItemsPerSection = 2;
                List<ErliDescriptionSection> SplitIntoSections(List<ErliDescriptionItem> items)
                {
                    var sectionsTemp = new List<ErliDescriptionSection>();
                    for (int i = 0; i < items.Count; i += maxItemsPerSection)
                    {
                        sectionsTemp.Add(new ErliDescriptionSection
                        {
                            Items = items.Skip(i).Take(maxItemsPerSection).ToList()
                        });
                    }
                    return sectionsTemp;
                }

                // Prepare description items
                var descriptionItems = offer.Descriptions?
                    .Where(d => d != null)
                    .OrderBy(d => d.DescriptionId)
                    .Select(d => new ErliDescriptionItem
                    {
                        Type = string.Equals(d.DescType, "IMAGE", StringComparison.OrdinalIgnoreCase) ? "IMAGE" : "TEXT",
                        Content = string.Equals(d.DescType, "TEXT", StringComparison.OrdinalIgnoreCase) ? d.Content : null,
                        Url = string.Equals(d.DescType, "IMAGE", StringComparison.OrdinalIgnoreCase) ? d.Content : null
                    })
                    .ToList() ?? new List<ErliDescriptionItem>();

                // Separate by type
                var textItems = descriptionItems.Where(i => i.Type == "TEXT").ToList();
                var imageItems = descriptionItems.Where(i => i.Type == "IMAGE").ToList();

                // Split into sections respecting API limit
                var sections = new List<ErliDescriptionSection>();
                sections.AddRange(SplitIntoSections(textItems));
                sections.AddRange(SplitIntoSections(imageItems));

                // Assign to product request
                productRequest.Description = new ErliDescription
                {
                    Sections = sections
                };

                var endpoint = $"products/{offer.ExternalId}";
                // Call Erli
                var response = await _erliClient.PostAsync<object>(endpoint, productRequest);

                Log.Information("Erli product created successfully. ExternalId: {ExternalId}", offer.ExternalId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create Erli product. ExternalId: {ExternalId}", offer.ExternalId);

                // Try to extract API error body from exception (if included)
                if (ex.Message.Contains("{") && ex.Message.Contains("}"))
                {
                    try
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                        if (match.Success)
                        {
                            var json = match.Value;

                            var parsed = JsonConvert.DeserializeObject<dynamic>(json);
                            if (parsed != null)
                            {
                                // Pretty print known fields
                                if (parsed.error != null)
                                    Log.Error("Erli API error: {Error}", (string)parsed.error);
                                if (parsed.details != null)
                                {
                                    foreach (var detail in parsed.details)
                                    {
                                        Log.Error("Field: {Field}, Message: {Message}",
                                            (string)detail.field,
                                            (string)detail.message);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        Log.Warning(parseEx, "Failed to parse Erli API error response for ExternalId: {ExternalId}", offer.ExternalId);
                    }
                }
            }
        }

        private List<ErliCategoryBreadcrumb> BuildBreadcrumb(int categoryId, Dictionary<int, AllegroCategory> categories)
        {
            var breadcrumb = new List<ErliCategoryBreadcrumb>();
            var current = categories.Values.FirstOrDefault(c => c.CategoryId == categoryId.ToString());

            while (current != null)
            {
                breadcrumb.Insert(0, new ErliCategoryBreadcrumb
                {
                    Id = current.CategoryId.ToString(),
                    Name = current.Name
                });

                if (current.ParentId == null) break;

                current = categories.Values.FirstOrDefault(c => c.Id == current.ParentId);
            }

            return breadcrumb;
        }
    }
}