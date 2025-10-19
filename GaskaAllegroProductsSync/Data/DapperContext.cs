using Microsoft.Data.SqlClient;
using System.Data;

namespace GaskaAllegroProductsSync.Data
{
    public class DapperContext
    {
        private readonly string _connectionString;

        public DapperContext(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("MyDbContext")
                ?? throw new InvalidOperationException("Connection string 'MyDbContext' not found.");
        }

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}