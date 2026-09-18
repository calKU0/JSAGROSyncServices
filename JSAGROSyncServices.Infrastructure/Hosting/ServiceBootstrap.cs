using DbUp;
using JSAGROSyncServices.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Data;

namespace JSAGROSyncServices.Infrastructure.Hosting
{
    /// <summary>
    /// Start wspólny dla wszystkich usług: konfiguracja logowania i migracje bazy.
    /// Dzięki temu każdy host robi to identycznie i poprawka trafia w jedno miejsce.
    /// </summary>
    public static class ServiceBootstrap
    {
        private const string MigrationLockName = "JSAGROSyncServices_Migrations";
        private const int MigrationLockTimeoutSeconds = 300;

        public static void ConfigureLogging(IConfiguration configuration)
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            var logsExpirationDays = Math.Max(configuration.GetValue<int?>("AppSettings:LogsExpirationDays") ?? 14, 1);

            // Poziom logowania z konfiguracji - "Debug" wlacza szczegoly per produkt/oferta,
            // przydatne przy diagnozie; domyslnie zostaje Information.
            var configuredLevel = configuration["AppSettings:MinimumLogLevel"];

            if (!Enum.TryParse<Serilog.Events.LogEventLevel>(configuredLevel, ignoreCase: true, out var minimumLevel))
                minimumLevel = Serilog.Events.LogEventLevel.Information;

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(minimumLevel)
                .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: logsExpirationDays,
                    shared: true,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Logging level: {Level}.", minimumLevel);
        }

        /// <summary>
        /// Uruchamia migracje pod blokadą aplikacyjną SQL Servera. Usługi startują razem,
        /// a DbUp sam się nie synchronizuje - bez blokady ten sam skrypt potrafi pójść dwa razy.
        /// </summary>
        public static void RunMigrations(string connectionString)
        {
            EnsureDatabase.For.SqlDatabase(connectionString);

            using var connection = new SqlConnection(connectionString);
            connection.Open();

            AcquireMigrationLock(connection);

            try
            {
                var upgrader = DeployChanges.To
                    .SqlDatabase(connectionString)
                    .LogTo(new SerilogUpgradeLog(Log.Logger))
                    .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Migrations"))
                    .Build();

                var result = upgrader.PerformUpgrade();

                if (!result.Successful)
                {
                    Log.Error(result.Error.ToString());
                    throw result.Error;
                }

                Log.Information("Database migration completed successfully.");
            }
            finally
            {
                ReleaseMigrationLock(connection);
            }
        }

        private static void AcquireMigrationLock(SqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "sp_getapplock";
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = MigrationLockTimeoutSeconds + 30;

            command.Parameters.AddWithValue("@Resource", MigrationLockName);
            command.Parameters.AddWithValue("@LockMode", "Exclusive");
            command.Parameters.AddWithValue("@LockOwner", "Session");
            command.Parameters.AddWithValue("@LockTimeout", MigrationLockTimeoutSeconds * 1000);

            var returnValue = command.Parameters.Add("@ReturnValue", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            command.ExecuteNonQuery();

            // 0 i 1 oznaczaja przyznana blokade; wartosci ujemne to blad lub timeout.
            var result = (int)(returnValue.Value ?? -999);

            if (result < 0)
                throw new InvalidOperationException($"Nie udało się uzyskać blokady na migracje bazy (kod {result}). Inna usługa może właśnie migrować bazę.");
        }

        private static void ReleaseMigrationLock(SqlConnection connection)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = "sp_releaseapplock";
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Resource", MigrationLockName);
                command.Parameters.AddWithValue("@LockOwner", "Session");

                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // Blokada i tak zniknie razem z sesją - nie ma sensu przerywać startu usługi.
                Log.Warning(ex, "Failed to release the migration lock.");
            }
        }
    }
}
