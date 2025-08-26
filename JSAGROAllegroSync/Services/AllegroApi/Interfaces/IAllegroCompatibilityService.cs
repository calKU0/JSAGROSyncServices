using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroCompatibilityService
    {
        Task FetchAndSaveCompatibleProducts(CancellationToken ct = default);
    }
}