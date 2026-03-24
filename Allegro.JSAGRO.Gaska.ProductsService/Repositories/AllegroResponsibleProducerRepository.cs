using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories
{
    public class AllegroResponsibleProducerRepository : IAllegroResponsibleProducerRepository
    {
        private readonly DapperContext _context;

        public AllegroResponsibleProducerRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task UpsertAllegroResponsibleProducers(IEnumerable<AllegroResponsibleProducer> producers, CancellationToken ct = default)
        {
            if (producers == null || !producers.Any())
            {
                return;
            }

            using var connection = _context.CreateConnection();
            await connection.ExecuteAsync(
                "AllegroResponsibleProducers_Upsert",
                producers.Select(p => new
                {
                    p.AllegroId,
                    Account = (int)p.Account,
                    p.Name,
                    p.TradeName,
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
