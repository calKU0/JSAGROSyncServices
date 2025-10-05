using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroErliSync.Data
{
    public class DapperContext
    {
        private readonly string _connectionString;

        public DapperContext()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString;
        }

        public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}