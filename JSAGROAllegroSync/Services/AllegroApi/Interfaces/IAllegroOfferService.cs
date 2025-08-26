using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroOfferService
    {
        Task SyncAllegroOffers(CancellationToken ct = default);

        Task CreateOffers(CancellationToken ct = default);

        Task UpdateOffers(CancellationToken ct = default);
    }
}