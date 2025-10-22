using DbUp.Engine.Output;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Logging
{
    public class SerilogUpgradeLog : IUpgradeLog
    {
        private readonly Serilog.ILogger _logger;

        public SerilogUpgradeLog(Serilog.ILogger logger)
        {
            _logger = logger;
        }

        public void LogTrace(string format, params object[] args)
            => _logger.Verbose(format, args);

        public void LogDebug(string format, params object[] args)
            => _logger.Debug(format, args);

        public void LogInformation(string format, params object[] args)
            => _logger.Information(format, args);

        public void LogWarning(string format, params object[] args)
            => _logger.Warning(format, args);

        public void LogError(string format, params object[] args)
            => _logger.Error(format, args);

        public void LogError(Exception ex, string format, params object[] args)
            => _logger.Error(ex, format, args);
    }
}