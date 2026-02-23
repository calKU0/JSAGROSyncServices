using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Repositories
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
                await connection.ExecuteAsync(
                    "RolmarProductParameters_Insert",
                    parameters,
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

        public async Task UpdateParameter(int id, int parameterId, string value, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(
                "RolmarProductParameters_Update",
                new
                {
                    Id = id,
                    CategoryParameterId = parameterId,
                    Value = value
                },
                commandType: CommandType.StoredProcedure);

            if (affectedRows == 0)
                throw new InvalidOperationException($"Parameter with Id {id} not found.");
        }

        public async Task DeleteParameter(string parameterName, int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteAsync(
                "RolmarProductParameters_DeleteByParameterName",
                new
                {
                    ProductId = productId,
                    ParameterName = parameterName
                },
                commandType: CommandType.StoredProcedure);
        }
    }
}