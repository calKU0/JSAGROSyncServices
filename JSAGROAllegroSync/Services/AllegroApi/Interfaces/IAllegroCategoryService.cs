using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroCategoryService
    {
        Task UpdateAllegroCategories(CancellationToken ct = default);

        Task FetchAndSaveCategoryParameters(CancellationToken ct = default);
    }
}