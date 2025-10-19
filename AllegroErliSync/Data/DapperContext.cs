using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace AllegroErliSync.Data
{
    public class DapperContext
    {
        private readonly string _connectionString;

        public DapperContext()
        {
            var baseConnection = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
            var builder = new SqlConnectionStringBuilder(baseConnection)
            {
                ConnectTimeout = 600
            };
            _connectionString = builder.ToString();
        }

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}