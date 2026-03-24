using Allegro.JSAGRO.Erli.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace Allegro.JSAGRO.Erli.ProductsService.Repositories
{
    public class AllegroResponsibleProducerRepository : IAllegroResponsibleProducerRepository
    {
        private readonly DapperContext _context;

        public AllegroResponsibleProducerRepository(DapperContext context)
        {
            _context = context;
        }

        public Task UpsertAllegroResponsibleProducers(IEnumerable<AllegroResponsibleProducer> producers, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<AllegroResponsibleProducer>> GetAllegroResponsibleProducers(CancellationToken ct = default)
        {
            using var connection = _context.CreateConnection();
            return await connection.QueryAsync<AllegroResponsibleProducer>(
                "AllegroResponsibleProducers_GetAll",
                new { Account = (int)ServiceConstants.Account },
                commandType: CommandType.StoredProcedure);
        }
    }
}
