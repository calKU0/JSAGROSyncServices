using System.ServiceProcess;

namespace GaskaAllegroSync
{
    internal static class Program
    {
        private static void Main()
        {
            var configuration = new Migrations.Configuration();
            var migrator = new System.Data.Entity.Migrations.DbMigrator(configuration);
            migrator.Update();

            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new GaskaAllegroSyncService()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}