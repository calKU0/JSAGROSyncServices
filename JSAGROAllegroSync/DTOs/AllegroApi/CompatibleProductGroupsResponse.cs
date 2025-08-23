using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs.AllegroApi
{
    public class CompatibleProductGroupsResponse
    {
        public List<CompatibleGroupDto> Groups { get; set; }

        public int Count { get; set; }

        public int TotalCount { get; set; }
    }

    public class CompatibleGroupDto
    {
        public string Id { get; set; }

        public string Text { get; set; }
    }
}