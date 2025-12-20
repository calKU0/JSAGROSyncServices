using System;
using System.Collections.Generic;
using System.Text;

namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface IAllegroProductService
    {
        Task SearchProducts(CancellationToken ct = default);
    }
}