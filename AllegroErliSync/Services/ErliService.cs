using AllegroErliSync.DTOs;
using AllegroErliSync.Mappers;
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

        private async Task SendProductToErli(Offer offer, bool isUpdate = false)
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
    }
}