using Microsoft.Data.SqlClient;
using System.Data;

namespace JSAGROSyncServices.Shared.Data
{
    public class DapperContext
    {
        private readonly string _connectionString;

        public DapperContext(string connectionString)
        {
            _connectionString = connectionString
                ?? throw new InvalidOperationException("Connection string 'MyDbContext' not found.");
        }

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}