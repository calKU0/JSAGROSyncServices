using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs
{
    public class DeviceCodeResponseDto
    {
        public string UserCode { get; set; }
        public string DeviceCode { get; set; }
        public int ExpiresIn { get; set; }
        public int Interval { get; set; }
        public string VerificationUri { get; set; }
        public string VerificationUriComplete { get; set; }
    }
}