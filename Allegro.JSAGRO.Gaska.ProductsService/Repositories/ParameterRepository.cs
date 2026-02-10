using Dapper;
using JSAGROSyncServices.Shared.Data;
using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Models;
using System.Data;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories
{
    public class ParameterRepository : IParameterRepository
    {
        private readonly DapperContext _context;

        public ParameterRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct)
        {
            if (parameters == null || parameters.Count == 0)
                return;

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var parameterRows = parameters.Select(p => new
                {
                    p.ProductId,
                    p.CategoryParameterId,
                    p.Value,
                    p.IsForProduct
                });

                await connection.ExecuteAsync(
                    "RolmarProductParameters_Insert",
                    parameterRows,
                    transaction,
                    commandType: CommandType.StoredProcedure);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task UpdateParameter(int productId, int parameterId, string value, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(
                "RolmarProductParameters_Update",
                new
                {
                    ProductId = productId,
                    ParameterId = parameterId,
                    Value = value
                },
                commandType: CommandType.StoredProcedure);

            if (affectedRows == 0)
                throw new InvalidOperationException($"Parameter with Id {productId} not found.");
        }
    }
}