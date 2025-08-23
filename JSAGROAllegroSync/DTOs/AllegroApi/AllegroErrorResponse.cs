using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs.AllegroApi
{
    public class AllegroErrorResponse
    {
        public List<AllegroError> Errors { get; set; }
    }

    public class AllegroError
    {
        public string Code { get; set; }
        public string Details { get; set; }
        public string Message { get; set; }
        public string Path { get; set; }
        public string UserMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; }
    }
}