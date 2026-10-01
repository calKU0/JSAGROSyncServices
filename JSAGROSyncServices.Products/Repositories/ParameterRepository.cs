using Dapper;
using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;
using System.Text.Json;

namespace JSAGROSyncServices.Products.Repositories
{
    public class ParameterRepository : IParameterRepository
    {
        private readonly DapperContext _context;
        private readonly ServiceContext _service;

        public ParameterRepository(DapperContext context, ServiceContext serviceContext)
        {
            _context = context;
            _service = serviceContext;
        }

        public async Task<int> FillDataIndependentParametersAsync(
            string countInOfferSqlPattern,
            string countInOfferValue,
            string mountingSideName,
            IReadOnlyList<string> universalMountingSides,
            CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "RolmarProductParameters_FillDataIndependent",
                    new
                    {
                        IntegrationCompany = _service.Company,
                        CountInOfferPattern = countInOfferSqlPattern,
                        CountInOfferValue = countInOfferValue,
                        MountingSideName = mountingSideName,
                        UniversalSides = JsonSerializer.Serialize(universalMountingSides)
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
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