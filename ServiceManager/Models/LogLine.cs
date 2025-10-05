using ServiceManager.Enums;

namespace ServiceManager.Models
{
    public class LogLine
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; }
    }
}