using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;
using System.Globalization;

namespace Allegro.JSAGRO.Gaska.ProductsService.Services.Allegro
{
    public class AllegroShippingRateService : IAllegroShippingRateService
    {
        private readonly ILogger<AllegroShippingRateService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IAllegroDeliveryMethodRepository _deliveryMethodRepo;
        public AllegroShippingRateService(ILogger<AllegroShippingRateService> logger, AllegroApiClient apiClient, IAllegroDeliveryMethodRepository deliveryMethodRepository)
        {
            _logger = logger;
            _apiClient = apiClient;
            _deliveryMethodRepo = deliveryMethodRepository;
        }
        public async Task SyncShippingRates(CancellationToken ct = default)
        {
            try
            {
                var allegroDeliveryMethods = new List<AllegroDeliveryMethod>();
                var shippingRates = await _apiClient.GetAsync<AllegroShippingRatesResponse>("/sale/shipping-rates", ct);
                var deliverymethods = await _apiClient.GetAsync<AllegroDeliveryMethodsResponse>("/sale/delivery-methods", ct);
                foreach (var shippingRate in shippingRates.ShippingRates)
                {
                    try
                    {
                        var shippingRateDetails = await _apiClient.GetAsync<AllegroShippingRateDetailsResponse>($"/sale/shipping-rates/{shippingRate.Id}", ct);
                        allegroDeliveryMethods.Add(MapShippingRateToModel(shippingRateDetails, deliverymethods));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error fetching details for shipping rate {shippingRate.Name}");
                    }
                }

                await _deliveryMethodRepo.UpsertAllegroDeliveryMethods(allegroDeliveryMethods, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing shipping rates from Allegro");
            }
        }

        private AllegroDeliveryMethod MapShippingRateToModel(AllegroShippingRateDetailsResponse details, AllegroDeliveryMethodsResponse deliveryMethods)
        {
            return new AllegroDeliveryMethod
            {
                AllegroId = details.Id,
                Account = ServiceConstants.Account,
                Name = details.Name,
                ManagedByAllegro = details.Feat?.ManagedByAllegro ?? false,
                IsFulfillment = details.Feat?.IsFulfillment ?? false,
                AllegroDeliveryMethodDetails = (details.Rates ?? Enumerable.Empty<AllegroShippingRateDetailsResponse.Rate>()).Select(r =>
                {
                    var matchedDeliveryMethod = deliveryMethods?.DeliveryMethods?.FirstOrDefault(dm => dm.Id == r.DeliveryMethod?.Id);

                    return new AllegroDeliveryMethodDetails
                    {
                        Name = matchedDeliveryMethod?.Name ?? string.Empty,
                        PaymentPolicy = ParsePaymentPolicy(matchedDeliveryMethod?.PaymentPolicy),
                        MaxPackageQuantity = r.MaxQuantityPerPackage,
                        MaxPackageWeight = ParseDecimalOrDefault(r.MaxPackageWeight?.Value),
                        MaxPackageWeightUnit = r.MaxPackageWeight?.Unit,
                        FirstItemAmount = ParseDecimalOrDefault(r.FirstItemRate?.Amount),
                        FirstItemCurrency = r.FirstItemRate?.Currency ?? string.Empty,
                        NextItemAmount = ParseDecimalOrDefault(r.NextItemRate?.Amount),
                        NextItemCurrency = r.NextItemRate?.Currency,
                        ShippingTimeFrom = r.ShippingTime?.From,
                        ShippingTimeTo = r.ShippingTime?.To
                    };
                }).ToList()
            };
        }

        private static decimal ParseDecimalOrDefault(string? value)
        {
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }

        private static PaymentPolicy ParsePaymentPolicy(string? value)
        {
            return Enum.TryParse<PaymentPolicy>(value, true, out var parsed)
                ? parsed
                : PaymentPolicy.IN_ADVANCE;
        }
    }
}
