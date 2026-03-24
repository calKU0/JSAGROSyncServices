using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;

namespace Allegro.JSAGRO.Gaska.ProductsService.Services.Allegro
{
    public class AllegroResponsibleProducerService : IAllegroResponsibleProducerService
    {
        private readonly ILogger<AllegroResponsibleProducerService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IAllegroResponsibleProducerRepository _responsibleProducerRepo;
        public AllegroResponsibleProducerService(ILogger<AllegroResponsibleProducerService> logger, AllegroApiClient apiClient, IAllegroResponsibleProducerRepository responsibleProducerRepo)
        {
            _logger = logger;
            _apiClient = apiClient;
            _responsibleProducerRepo = responsibleProducerRepo;
        }
        public async Task SyncResponsibleProducers(CancellationToken ct = default)
        {
            try
            {
                var responsibleProducers = new List<AllegroResponsibleProducer>();
                var allegroResponsibleProducers = _apiClient.GetAsync<AllegroResponsibleProducersResponse>("/sale/responsible-producers", ct).Result;
                foreach (var rp in allegroResponsibleProducers.ResponsibleProducers)
                {
                    responsibleProducers.Add(new AllegroResponsibleProducer
                    {
                        AllegroId = rp.Id,
                        Account = ServiceConstants.Account,
                        Name = rp.Name,
                        TradeName = rp.ProducerData.TradeName,
                        CountryCode = rp.ProducerData.Address.CountryCode,
                        Street = rp.ProducerData.Address.Street,
                        PostalCode = rp.ProducerData.Address.PostalCode,
                        City = rp.ProducerData.Address.City,
                        Email = rp.ProducerData.Contact.Email,
                        Phone = rp.ProducerData.Contact.PhoneNumber,
                        FormUrl = rp.ProducerData.Contact.FormUrl
                    });
                }

                await _responsibleProducerRepo.UpsertAllegroResponsibleProducers(responsibleProducers, ct);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing responsible persons from Allegro");
            }
        }
    }
}
