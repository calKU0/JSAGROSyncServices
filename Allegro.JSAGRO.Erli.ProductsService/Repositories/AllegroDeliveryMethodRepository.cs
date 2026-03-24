using Allegro.JSAGRO.Erli.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace Allegro.JSAGRO.Erli.ProductsService.Repositories
{
    public class AllegroDeliveryMethodRepository : IAllegroDeliveryMethodRepository
    {
        private readonly DapperContext _context;

        public AllegroDeliveryMethodRepository(DapperContext context)
        {
            _context = context;
        }

        public Task UpsertAllegroDeliveryMethods(IEnumerable<AllegroDeliveryMethod> deliveryMethods, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<AllegroDeliveryMethod>> GetAllegroDeliveryMethods(CancellationToken ct = default)
        {
            using var connection = _context.CreateConnection();

            using var grid = await connection.QueryMultipleAsync(
                "AllegroDeliveryMethods_GetAll",
                new { Account = (int)ServiceConstants.Account },
                commandType: CommandType.StoredProcedure);

            var methods = grid.Read<AllegroDeliveryMethod>().ToList();
            var details = grid.Read<AllegroDeliveryMethodDetails>().ToList();
            var detailsLookup = details.ToLookup(x => x.AllegroDeliveryMethodId);

            foreach (var method in methods)
            {
                method.AllegroDeliveryMethodDetails = detailsLookup[method.Id].ToList();
            }

            return methods;
        }
    }
}
