using System;
using System.Collections.Generic;
using System.Text;

namespace RolmarAllegroProductsSyncService.Services.Interfaces
{
    public interface ISyncStateService
    {
        Task<string?> GetLastCategoriesNameAsync();

        Task SetLastCategoriesNameAsync(string categoriesName);
    }
}