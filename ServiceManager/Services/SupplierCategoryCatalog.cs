using Microsoft.Data.SqlClient;
using ServiceManager.Models;
using System.Data;

namespace ServiceManager.Services
{
    /// <summary>
    /// Lista kategorii dostawcy z bazy. Serwisy utrzymują ją na bieżąco: Gąska z API /categories,
    /// Rolmar z kategorii produktów zwracanych przez API.
    /// </summary>
    public class SupplierCategoryCatalog
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        public async Task<IReadOnlyList<CategoryOption>> LoadAsync(string connectionString, int integrationCompany, CancellationToken ct = default)
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                // Konfigurator ma odpowiadać szybko - lepiej pokazać błąd niż wisieć na połączeniu.
                ConnectTimeout = (int)Timeout.TotalSeconds
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);

            await using var command = new SqlCommand("dbo.SupplierCategories_GetByCompany", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = (int)Timeout.TotalSeconds
            };

            command.Parameters.AddWithValue("@IntegrationCompany", integrationCompany);

            var result = new List<CategoryOption>();

            await using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
                result.Add(new CategoryOption(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));

            return result;
        }
    }
}
