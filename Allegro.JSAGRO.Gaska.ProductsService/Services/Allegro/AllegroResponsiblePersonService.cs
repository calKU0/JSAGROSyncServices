using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;

namespace Allegro.JSAGRO.Gaska.ProductsService.Services.Allegro
{
    public class AllegroResponsiblePersonService : IAllegroResponsiblePersonService
    {
        private readonly ILogger<AllegroResponsiblePersonService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IAllegroResponsiblePersonRepository _responsiblePersonRepo;
        public AllegroResponsiblePersonService(ILogger<AllegroResponsiblePersonService> logger, AllegroApiClient apiClient, IAllegroResponsiblePersonRepository responsiblePersonRepo)
        {
            _logger = logger;
            _apiClient = apiClient;
            _responsiblePersonRepo = responsiblePersonRepo;
        }
        public async Task SyncResponsiblePersons(CancellationToken ct = default)
        {
            try
            {
                var responsiblePersons = new List<AllegroResponsiblePerson>();
                var allegroResponsiblePersons = _apiClient.GetAsync<AllegroResponsiblePersonsResult>("/sale/responsible-persons", ct).Result;
                foreach (var rp in allegroResponsiblePersons.ResponsiblePersons)
                {
                    responsiblePersons.Add(new AllegroResponsiblePerson
                    {
                        AllegroId = rp.Id,
                        Account = ServiceConstants.Account,
                        Name = rp.Name,
                        PersonName = rp.PersonalData.Name,
                        CountryCode = rp.PersonalData.Address.CountryCode,
                        Street = rp.PersonalData.Address.Street,
                        PostalCode = rp.PersonalData.Address.PostalCode,
                        City = rp.PersonalData.Address.City,
                        Email = rp.PersonalData.Contact.Email,
                        Phone = rp.PersonalData.Contact.PhoneNumber,
                        FormUrl = rp.PersonalData.Contact.FormUrl
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
