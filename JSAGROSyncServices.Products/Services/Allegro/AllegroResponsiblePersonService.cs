using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    public class AllegroResponsiblePersonService : IAllegroResponsiblePersonService
    {
        private readonly ServiceContext _service;
        private readonly ILogger<AllegroResponsiblePersonService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IAllegroResponsiblePersonRepository _responsiblePersonRepo;
        public AllegroResponsiblePersonService(ILogger<AllegroResponsiblePersonService> logger, AllegroApiClient apiClient, IAllegroResponsiblePersonRepository responsiblePersonRepo, ServiceContext serviceContext)
        {
            _service = serviceContext;
            _logger = logger;
            _apiClient = apiClient;
            _responsiblePersonRepo = responsiblePersonRepo;
        }
        public async Task SyncResponsiblePersons(CancellationToken ct = default)
        {
            try
            {
                var responsiblePersons = new List<AllegroResponsiblePerson>();
                var allegroResponsiblePersons = await _apiClient.GetAsync<AllegroResponsiblePersonsResult>("/sale/responsible-persons", ct);

                if (allegroResponsiblePersons?.ResponsiblePersons == null)
                {
                    _logger.LogError("Allegro returned no responsible persons.");
                    return;
                }

                foreach (var rp in allegroResponsiblePersons.ResponsiblePersons)
                {
                    responsiblePersons.Add(new AllegroResponsiblePerson
                    {
                        AllegroId = rp.Id ?? string.Empty,
                        Account = _service.Account,
                        Name = rp.Name ?? string.Empty,
                        PersonName = rp.PersonalData?.Name ?? string.Empty,
                        CountryCode = rp.PersonalData?.Address?.CountryCode ?? string.Empty,
                        Street = rp.PersonalData?.Address?.Street ?? string.Empty,
                        PostalCode = rp.PersonalData?.Address?.PostalCode ?? string.Empty,
                        City = rp.PersonalData?.Address?.City ?? string.Empty,
                        Email = rp.PersonalData?.Contact?.Email,
                        Phone = rp.PersonalData?.Contact?.PhoneNumber,
                        FormUrl = rp.PersonalData?.Contact?.FormUrl
                    });
                }

                await _responsiblePersonRepo.UpsertAllegroResponsiblePersons(responsiblePersons, ct);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing responsible persons from Allegro");
            }
        }
    }
}
