using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    public class AllegroResponsibleProducerService : IAllegroResponsibleProducerService
    {
        private readonly ServiceContext _service;
        private readonly ILogger<AllegroResponsibleProducerService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IAllegroResponsibleProducerRepository _responsibleProducerRepo;
        public AllegroResponsibleProducerService(ILogger<AllegroResponsibleProducerService> logger, AllegroApiClient apiClient, IAllegroResponsibleProducerRepository responsibleProducerRepo, ServiceContext serviceContext)
        {
            _service = serviceContext;
            _logger = logger;
            _apiClient = apiClient;
            _responsibleProducerRepo = responsibleProducerRepo;
        }
        public async Task SyncResponsibleProducers(CancellationToken ct = default)
        {
            try
            {
                var responsibleProducers = new List<AllegroResponsibleProducer>();
                var allegroResponsibleProducers = await _apiClient.GetAsync<AllegroResponsibleProducersResponse>("/sale/responsible-producers", ct);

                if (allegroResponsibleProducers?.ResponsibleProducers == null)
                {
                    _logger.LogError("Allegro returned no responsible producers.");
                    return;
                }

                foreach (var rp in allegroResponsibleProducers.ResponsibleProducers)
                {
                    responsibleProducers.Add(new AllegroResponsibleProducer
                    {
                        AllegroId = rp.Id ?? string.Empty,
                        Account = _service.Account,
                        Name = rp.Name ?? string.Empty,
                        TradeName = rp.ProducerData?.TradeName ?? string.Empty,
                        CountryCode = rp.ProducerData?.Address?.CountryCode ?? string.Empty,
                        Street = rp.ProducerData?.Address?.Street ?? string.Empty,
                        PostalCode = rp.ProducerData?.Address?.PostalCode ?? string.Empty,
                        City = rp.ProducerData?.Address?.City ?? string.Empty,
                        Email = rp.ProducerData?.Contact?.Email,
                        Phone = rp.ProducerData?.Contact?.PhoneNumber,
                        FormUrl = rp.ProducerData?.Contact?.FormUrl
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
