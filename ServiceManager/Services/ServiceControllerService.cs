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
            DisposeController();
            if (!string.IsNullOrWhiteSpace(serviceName))
                _controller = new ServiceController(serviceName);
        }

        public async Task<ServiceControllerStatus?> GetStatusAsync()
        {
            if (_controller == null)
                return null;

            return await Task.Run(() =>
            {
                lock (_lock)
                {
                    _controller.Refresh();
                    return _controller.Status;
                }
            });
        }

        public async Task RunOperationAsync(Action<ServiceController> operation)
        {
            if (_controller == null)
                return;

            await Task.Run(() =>
            {
                lock (_lock)
                {
                    operation(_controller);
                }
            });
        }

        public void Dispose()
        {
            DisposeController();
        }

        private void DisposeController()
        {
            _controller?.Close();
            _controller?.Dispose();
            _controller = null;
        }
    }
}
