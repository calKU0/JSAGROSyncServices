using System;
using System.Collections.Generic;
using System.Text;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Services.Interfaces
{
    public interface ISyncStateService
    {
        Task<string?> GetLastCategoriesNameAsync();

        Task SetLastCategoriesNameAsync(string categoriesName);
    }
}