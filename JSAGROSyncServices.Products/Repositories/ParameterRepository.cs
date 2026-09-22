using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace JSAGROSyncServices.Products.Repositories
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

        /// <summary>
        /// Zwraca true, gdy parametr faktycznie zmienil wartosc. Brak parametru w kategorii
        /// produktu nie jest bledem - ten sam parametr Allegro ma rozne id w roznych kategoriach.
        /// </summary>
        public async Task<bool> UpdateParameter(int productId, int parameterId, string value, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            var updatedRows = await connection.ExecuteScalarAsync<int>(
                "RolmarProductParameters_Update",
                new
                {
                    ProductId = productId,
                    ParameterId = parameterId,
                    Value = value
                },
                commandType: CommandType.StoredProcedure);

            return updatedRows > 0;
        }

        public async Task<bool> UpdateParameterByName(int productId, string parameterName, string value, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            var updatedRows = await connection.ExecuteScalarAsync<int>(
                "RolmarProductParameters_UpdateByName",
                new
                {
                    ProductId = productId,
                    ParameterName = parameterName,
                    Value = value
                },
                commandType: CommandType.StoredProcedure);

            return updatedRows > 0;
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