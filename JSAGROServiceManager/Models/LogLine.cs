using JSAGROAllegroServiceConfiguration.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroServiceConfiguration.Models
{
    public class LogLine
    {
        public LogLevel Level { get; set; }
        public string Message { get; set; }
    }
}