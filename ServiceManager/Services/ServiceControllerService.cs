using System;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace ServiceManager.Services
{
    public class ServiceControllerService : IDisposable
    {
        private readonly object _lock = new();
        private ServiceController? _controller;

        public void SetService(string serviceName)
        {
            // Pod lockiem, bo status odswieza sie w tle na innym watku.
            lock (_lock)
            {
                DisposeController();
                if (!string.IsNullOrWhiteSpace(serviceName))
                    _controller = new ServiceController(serviceName);
            }
        }

        /// <summary>
        /// Stan usługi. Sprawdzenie i użycie kontrolera są pod tym samym lockiem - inaczej
        /// <see cref="SetService"/> mógłby go zwolnić między jednym a drugim.
        /// </summary>
        public async Task<ServiceControllerStatus?> GetStatusAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        if (_controller == null)
                            return (ServiceControllerStatus?)null;

                        _controller.Refresh();
                        return _controller.Status;
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Kontroler zwolniono w trakcie odczytu - dla ekranu to po prostu brak stanu.
                return null;
            }
        }

        public async Task RunOperationAsync(Action<ServiceController> operation)
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_lock)
                    {
                        if (_controller == null)
                            return;

                        operation(_controller);
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Kontroler zwolniono w trakcie operacji - nie ma czego ponawiać.
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                DisposeController();
            }
        }

        private void DisposeController()
        {
            _controller?.Close();
            _controller?.Dispose();
            _controller = null;
        }
    }
}
