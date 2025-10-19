using System.ServiceProcess;

namespace AllegroErliSync
{
    internal static class Program
    {
        /// <summary>
        /// Główny punkt wejścia dla aplikacji.
        /// </summary>
        private static void Main()
        {
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[]
            {
                new AllegroErliSyncService()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}