using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Services.Interfaces
{
    public interface IOrderService
    {
        public Task SyncOrdersFromAllegro(CancellationToken ct = default!);

        public Task CreateOrdersInGaska(CancellationToken ct = default!);

        public Task UpdateOrdersInAllegro(CancellationToken ct = default!);

        public Task UpdateOrderGaskaInfo(CancellationToken ct = default!);
    }
}