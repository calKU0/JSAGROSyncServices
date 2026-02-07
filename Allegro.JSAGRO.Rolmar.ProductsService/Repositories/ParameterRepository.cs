using Dapper;
using JSAGROSyncServices.Shared.Data;
using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Models;

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

            const string sql = @"
            INSERT INTO RolmarProductParameters
                (ProductId, CategoryParameterId, Value, IsForProduct)
            VALUES
                (@ProductId, @CategoryParameterId, @Value, @IsForProduct);
            ";

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                await connection.ExecuteAsync(sql, parameters, transaction);
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
            const string sql = @"
                UPDATE RolmarProductParameters
                SET CategoryParameterId = @ParameterId,
                    Value = @Value
                WHERE Id = @Id;
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(sql, new
            {
                Id = id,
                ParameterId = parameterId,
                Value = value
            });

            if (affectedRows == 0)
                throw new InvalidOperationException($"Parameter with Id {id} not found.");
        }
    }
}