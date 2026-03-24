using Allegro.JSAGRO.Erli.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace Allegro.JSAGRO.Erli.ProductsService.Repositories
{
    public class AllegroResponsiblePersonRepository : IAllegroResponsiblePersonRepository
    {
        private readonly DapperContext _context;

        public AllegroResponsiblePersonRepository(DapperContext context)
        {
            _context = context;
        }

        public Task UpsertAllegroResponsiblePersons(IEnumerable<AllegroResponsiblePerson> responsiblePersons, CancellationToken ct = default)
        {
            throw new NotImplementedException();
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
