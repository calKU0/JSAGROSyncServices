using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.AllegroApi
{
    public class ApiResult<T>
    {
        public T? Data { get; set; }
        public HttpResponseMessage Response { get; set; } = default!;
        public string Body { get; set; } = string.Empty;
    }
}