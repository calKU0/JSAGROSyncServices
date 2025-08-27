using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroServiceConfiguration.Models
{
    public class LogFileItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public int WarningsCount { get; set; }
        public int ErrorsCount { get; set; }
    }
}