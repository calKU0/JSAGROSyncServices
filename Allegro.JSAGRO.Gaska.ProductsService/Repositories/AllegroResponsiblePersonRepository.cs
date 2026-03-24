using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories
{
    public class AllegroResponsiblePersonRepository : IAllegroResponsiblePersonRepository
    {
        private readonly DapperContext _context;

        public AllegroResponsiblePersonRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task UpsertAllegroResponsiblePersons(IEnumerable<AllegroResponsiblePerson> responsiblePersons, CancellationToken ct = default)
        {
            if (responsiblePersons == null || !responsiblePersons.Any())
            {
                return;
            }

            using var connection = _context.CreateConnection();
            await connection.ExecuteAsync(
                "AllegroResponsiblePersons_Upsert",
                responsiblePersons.Select(p => new
                {
                    p.AllegroId,
                    Account = (int)p.Account,
                    p.Name,
                    p.PersonName,
                    p.CountryCode,
                    p.Street,
                    p.PostalCode,
                    p.City,
                    p.Email,
                    p.Phone,
                    p.FormUrl
                }),
                commandType: CommandType.StoredProcedure);
        }

        public async Task<IEnumerable<AllegroResponsiblePerson>> GetAllegroResponsiblePersons(CancellationToken ct = default)
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<AllegroResponsiblePerson>(
                "AllegroResponsiblePersons_GetAll",
                new { Account = (int)ServiceConstants.Account },
                commandType: CommandType.StoredProcedure);
        }
    }
}
