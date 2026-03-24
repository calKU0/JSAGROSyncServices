using Allegro.JSAGRO.Erli.ProductsService.DTOs;
using Allegro.JSAGRO.Erli.ProductsService.Enums;
using Allegro.JSAGRO.Erli.ProductsService.Mappers;
using Allegro.JSAGRO.Erli.ProductsService.Repositories;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using Newtonsoft.Json;
using Serilog;
using System.Globalization;
using System.Text;

namespace Allegro.JSAGRO.Erli.ProductsService.Services
{
    public class ErliService
    {
        private readonly ErliClient _erliClient;
        private readonly OfferRepository _offerRepository;
        private readonly IAllegroResponsiblePersonRepository _personRepo;
        private readonly IAllegroResponsibleProducerRepository _producerRepo;
        private readonly IAllegroDeliveryMethodRepository _deliveryRepo;

        public ErliService(OfferRepository offerRepository, ErliClient erliClient, IAllegroResponsiblePersonRepository personRepo, IAllegroResponsibleProducerRepository producerRepo, IAllegroDeliveryMethodRepository deliveryRepo)
        {
            _offerRepository = offerRepository ?? throw new ArgumentNullException(nameof(offerRepository));
            _erliClient = erliClient ?? throw new ArgumentNullException(nameof(erliClient));
            _personRepo = personRepo;
            _producerRepo = producerRepo;
            _deliveryRepo = deliveryRepo;
        }

        public async Task SyncResponsibleProducersWithErli()
        {
            Log.Information("Fetching responsible producers from database...");
            var allegroProducers = await _producerRepo.GetAllegroResponsibleProducers();
            Log.Information("Total responsible producers fetched: {Count}", allegroProducers.Count());

            var erliProducers = await _erliClient.GetAsync<List<ErliResponsibleProducerResponse>>("dictionaries/responsibleProducers");

            foreach (var producer in allegroProducers)
            {
                try
                {
                    var existing = erliProducers?.FirstOrDefault(x => x.IdempotenceKey == producer.AllegroId);

                    if (existing == null)
                    {
                        var createRequest = new ErliResponsibleProducerCreate
                        {
                            Name = producer.TradeName,
                            IdempotenceKey = producer.AllegroId,
                            ProperName = producer.Name,
                            Country = producer.CountryCode,
                            Address = producer.Street,
                            PostalCode = producer.PostalCode,
                            City = producer.City,
                            Phone = producer.Phone,
                            Email = producer.Email,
                            Source = "allegro"
                        };

                        await _erliClient.PostAsync<object>("dictionaries/responsibleProducers", createRequest);
                        continue;
                    }

                    var patchRequest = new ErliResponsibleProducerPatch
                    {
                        Name = producer.TradeName,
                        IdempotenceKey = producer.AllegroId,
                        ProperName = producer.Name,
                        Country = producer.CountryCode,
                        Address = producer.Street,
                        PostalCode = producer.PostalCode,
                        City = producer.City,
                        Phone = producer.Phone,
                        Email = producer.Email,
                        Source = "allegro"
                    };

                    await _erliClient.PatchAsync<object>($"dictionaries/responsibleProducers/{existing.Id}", patchRequest);
                } // Closing brace for SyncResponsibleProducersWithErli
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to sync responsible producer. AllegroId: {AllegroId}", producer.AllegroId);
                }
            }
        }

        public async Task SyncDeliveriesWithErli()
        {
            Log.Information("Fetching delivery methods from database...");
            var deliveryMethods = await _deliveryRepo.GetAllegroDeliveryMethods();
            Log.Information("Total delivery methods fetched: {Count}", deliveryMethods.Count());

            var erliPriceLists = await _erliClient.GetAsync<List<ErliPriceListResponse>>("delivery/priceListsDetails");

            foreach (var deliveryMethod in deliveryMethods)
            {
                try
                {
                    var existingPriceList = erliPriceLists?.FirstOrDefault(priceList =>
                        string.Equals(priceList.Name?.Trim(), deliveryMethod.Name?.Trim(), StringComparison.OrdinalIgnoreCase));

                    var pricesByMethod = new Dictionary<ErliDeliveryMethod, Prices>();

                    foreach (var detail in deliveryMethod.AllegroDeliveryMethodDetails ?? Enumerable.Empty<AllegroDeliveryMethodDetails>())
                    {
                        if (!TryMapErliDeliveryMethod(detail, out var erliDeliveryMethod))
                        {
                            Log.Warning("Unknown Erli delivery method enum value: {DeliveryName}", detail.Name);
                            continue;
                        }

                        var existingPrice = existingPriceList?.Prices?
                            .FirstOrDefault(p => p.DeliveryMethod?.Id == erliDeliveryMethod);

                        var mappedPrice = new Prices
                        {
                            DeliveryMethod = new DeliveryMethod
                            {
                                Id = erliDeliveryMethod,
                                DeliveryTime = existingPrice?.DeliveryMethod?.DeliveryTime
                            },
                            BasePrice = Convert.ToInt32(Math.Round(detail.FirstItemAmount * 100, MidpointRounding.AwayFromZero)),
                            NextItemPrice = Convert.ToInt32(Math.Round((detail.NextItemAmount ?? 0) * 100, MidpointRounding.AwayFromZero)),
                            Limit = BuildErliLimit(erliDeliveryMethod, detail.MaxPackageQuantity),
                            NextDayDeliveryOption = existingPrice?.NextDayDeliveryOption
                        };

                        if (pricesByMethod.TryGetValue(erliDeliveryMethod, out var existingMappedPrice))
                        {
                            var shouldReplace = mappedPrice.BasePrice < existingMappedPrice.BasePrice
                                || (mappedPrice.BasePrice == existingMappedPrice.BasePrice && mappedPrice.NextItemPrice < existingMappedPrice.NextItemPrice);

                            if (shouldReplace)
                            {
                                pricesByMethod[erliDeliveryMethod] = mappedPrice;
                            }

                            //Log.Warning("Duplicate mapped Erli delivery method {ErliMethod} in price list {PriceListName}. Keeping cheapest option.", erliDeliveryMethod, deliveryMethod.Name);
                            continue;
                        }

                        pricesByMethod[erliDeliveryMethod] = mappedPrice;
                    }

                    var prices = pricesByMethod.Values.ToList();

                    if (!prices.Any())
                    {
                        Log.Warning("Skipping delivery sync for {DeliveryMethodName}. No mappable prices.", deliveryMethod.Name);
                        continue;
                    }

                    if (existingPriceList == null)
                    {
                        var createRequest = new ErliPriceListCreate
                        {
                            Name = deliveryMethod.Name,
                            Prices = prices,
                            ErliProEnabled = false,
                            NextDayDeliveryEnabled = false
                        };

                        await _erliClient.PostAsync<object>("delivery/priceList", createRequest);
                        continue;
                    }

                    var patchRequest = new ErliPriceListPatch
                    {
                        Prices = prices,
                        ErliProEnabled = existingPriceList.ErliProEnabled,
                        NextDayDeliveryEnabled = existingPriceList.NextDayDeliveryEnabled
                    };

                    await _erliClient.PatchAsync<object>($"delivery/priceList/{existingPriceList.Id}", patchRequest);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to sync delivery method. Name: {DeliveryMethodName}", deliveryMethod.Name);
                }
            }
        }


        public async Task SyncResponsiblePersonsWithErli()
        {
            Log.Information("Fetching responsible persons from database...");
            var allegroPersons = await _personRepo.GetAllegroResponsiblePersons();
            Log.Information("Total responsible persons fetched: {Count}", allegroPersons.Count());

            var erliPersons = await _erliClient.GetAsync<List<ErliResponsiblePersonResponse>>("dictionaries/responsiblePersons");

            foreach (var person in allegroPersons)
            {
                try
                {
                    var existing = erliPersons?.FirstOrDefault(x => x.IdempotenceKey == person.AllegroId);

                    if (existing == null)
                    {
                        var createRequest = new ErliResponsiblePersonCreate
                        {
                            Name = person.PersonName,
                            IdempotenceKey = person.AllegroId,
                            ProperName = person.Name,
                            Country = person.CountryCode,
                            Address = person.Street,
                            PostalCode = person.PostalCode,
                            City = person.City,
                            Phone = person.Phone,
                            Email = person.Email,
                            Source = "allegro"
                        };

                        await _erliClient.PostAsync<object>("dictionaries/responsiblePersons", createRequest);
                        continue;
                    }

                    var patchRequest = new ErliResponsiblePersonPatch
                    {
                        Name = person.PersonName,
                        IdempotenceKey = person.AllegroId,
                        ProperName = person.Name,
                        Country = person.CountryCode,
                        Address = person.Street,
                        PostalCode = person.PostalCode,
                        City = person.City,
                        Phone = person.Phone,
                        Email = person.Email,
                        Source = "allegro"
                    };

                    await _erliClient.PatchAsync<object>($"dictionaries/responsiblePersons/{existing.Id}", patchRequest);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to sync responsible person. AllegroId: {AllegroId}", person.AllegroId);
                }
            }
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
                    await SendProductToErli(offer);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get offers to create in erli.");
            }
        }

        public async Task UpdateProductsInErli()
        {
            try
            {
                Log.Information("Fetching offers for Erli product update...");
                var offersToUpdate = _offerRepository.GetOffersForErliUpdate().ToList();
                Log.Information("Total offers to update in Erli: {Count}", offersToUpdate.Count);

                foreach (var offer in offersToUpdate)
                {
                    await SendProductToErli(offer, true);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to get offers to update in erli.");
            }
        }

        private async Task SendProductToErli(AllegroOffer offer, bool isUpdate = false)
        {
            var request = ErliProductMapper.MapFromOffer(offer);

            var endpoint = $"products/{offer.Id}";

            try
            {
                if (isUpdate)
                    await _erliClient.PatchAsync<object>(endpoint, request);
                else
                    await _erliClient.PostAsync<object>(endpoint, request);

                Log.Information("Erli product {Action} successfully. Name: {Name}", isUpdate ? "updated" : "created", offer.Name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to {Action} Erli product. Name: {Name}", isUpdate ? "update" : "create", offer.Name);
                ParseErliApiError(ex, offer.ExternalId);
            }
        }

        private void ParseErliApiError(Exception ex, string externalId)
        {
            if (ex == null || string.IsNullOrWhiteSpace(ex.Message)) return;
            if (!ex.Message.Contains("{") || !ex.Message.Contains("}")) return;

            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    ex.Message, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

                if (!match.Success) return;

                var parsed = JsonConvert.DeserializeObject<dynamic>(match.Value);
                if (parsed.error != null)
                {
                    Log.Error("Erli API error: {Error}", (string)parsed.error);
                }

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
            catch (Exception parseEx)
            {
                Log.Warning(parseEx, "Failed to parse Erli API error response for ExternalId: {ExternalId}", externalId);
            }
        }

        private static bool TryMapErliDeliveryMethod(AllegroDeliveryMethodDetails detail, out ErliDeliveryMethod method)
        {
            method = default;
            if (string.IsNullOrWhiteSpace(detail?.Name))
            {
                return false;
            }

            var normalized = NormalizeDeliveryName(detail.Name);
            var isCod = detail.PaymentPolicy == PaymentPolicy.CASH_ON_DELIVERY || normalized.Contains("pobranie") || normalized.Contains("cod");

            // personal pickup
            if (normalized.Contains("odbior osobisty"))
            {
                method = isCod ? ErliDeliveryMethod.odbiorOsobistyCod : ErliDeliveryMethod.odbiorOsobisty;
                return true;
            }

            // inpost
            if (normalized.Contains("paczkomaty inpost") || normalized.Contains("paczkomat"))
            {
                method = isCod ? ErliDeliveryMethod.paczkomatCod : ErliDeliveryMethod.paczkomat;
                return true;
            }

            if (normalized.Contains("inpost"))
            {
                method = isCod ? ErliDeliveryMethod.inPostCod : ErliDeliveryMethod.inPost;
                return true;
            }

            // dpd pickup/point/locker
            if (normalized.Contains("dpd pickup") || normalized.Contains("one punkt, dpd") || normalized.Contains("automaty paczkowe dpd") || normalized.Contains("one box, dpd") || normalized.Contains("punkt dpd"))
            {
                method = isCod ? ErliDeliveryMethod.dpdPunktCod : ErliDeliveryMethod.dpdPunkt;
                return true;
            }

            // dpd courier
            if (normalized.Contains("dpd"))
            {
                method = isCod ? ErliDeliveryMethod.dpdCod : ErliDeliveryMethod.dpd;
                return true;
            }

            // dhl
            if (normalized.Contains("dhl box") || normalized.Contains("automat dhl") || normalized.Contains("punkcie dhl"))
            {
                method = ErliDeliveryMethod.erliDHLPunktyOdbioru10kg;
                return true;
            }

            if (normalized.Contains("dhl"))
            {
                method = isCod ? ErliDeliveryMethod.dhlCod : ErliDeliveryMethod.dhl;
                return true;
            }

            // orlen
            if (normalized.Contains("orlen paczka"))
            {
                method = isCod ? ErliDeliveryMethod.orlenPaczkaCod : ErliDeliveryMethod.orlenPaczka;
                return true;
            }

            // pocztex / poczta
            if (normalized.Contains("pocztex") || normalized.Contains("poczta polska punkt"))
            {
                method = isCod ? ErliDeliveryMethod.pocztaPolskaPunktCod : ErliDeliveryMethod.pocztaPolskaPunkt;
                return true;
            }

            // other carriers
            if (normalized.Contains("fedex"))
            {
                method = isCod ? ErliDeliveryMethod.fedexCod : ErliDeliveryMethod.fedex;
                return true;
            }

            if (normalized.Contains("gls"))
            {
                method = isCod ? ErliDeliveryMethod.glsCod : ErliDeliveryMethod.gls;
                return true;
            }

            if (normalized.Contains("przesylka kurierska") || normalized.Contains("kurier") || normalized.Contains("international") || normalized.Contains("wysylka z polski") || normalized.Contains("packeta"))
            {
                method = isCod ? ErliDeliveryMethod.courierCod : ErliDeliveryMethod.courier;
                return true;
            }

            return false;
        }

        private static string NormalizeDeliveryName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var withoutDiacritics = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(withoutDiacritics.Length);
            foreach (var c in withoutDiacritics)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.ToString()
                .Replace("(ad)", string.Empty)
                .Replace("-", " ")
                .Replace(",", " ")
                .Replace("  ", " ")
                .Trim();
        }

        private static object BuildErliLimit(ErliDeliveryMethod method, int? maxPackageQuantity)
        {
            var limit = Math.Max(1, maxPackageQuantity ?? 1);

            if (method == ErliDeliveryMethod.erliPaczkomat)
            {
                return new List<DeliveryDimensionLimit>
                {
                    new() { Dimension = "A", Limit = limit },
                    new() { Dimension = "B", Limit = limit },
                    new() { Dimension = "C", Limit = limit }
                };
            }

            return limit;
        }
    }
}