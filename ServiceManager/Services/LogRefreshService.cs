using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace ServiceManager.Services
{
    public class LogRefreshService
    {
        private DispatcherTimer? _timer;
        private Func<Task>? _onTick;
        private bool _tickInProgress;

        public void Start(TimeSpan interval, Func<Task> onTick)
        {
            Stop();
            _onTick = onTick;
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += HandleTick;
            _timer.Start();
        }

        public void Stop()
        {
            if (_timer == null)
                return;

            _timer.Tick -= HandleTick;
            _timer.Stop();
            _timer = null;
            _onTick = null;
        }

        private async void HandleTick(object? sender, EventArgs e)
        {
            // async void - wyjatek stad nie ma gdzie wyplynac i ubilby cala aplikacje.
            if (_onTick == null || _tickInProgress)
                return;

            _tickInProgress = true;

            try
            {
                await _onTick();
            }
            catch
            {
                // Odswiezanie logow nie moze przerwac pracy konfiguratora.
            }
            finally
            {
                _tickInProgress = false;
            }
        }
    }
}
