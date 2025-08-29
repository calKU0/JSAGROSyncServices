using System.ServiceProcess;

namespace JSAGROAllegroSync
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
                new JSAGROAllegroService()
            };
            ServiceBase.Run(ServicesToRun);
        }
    }
}