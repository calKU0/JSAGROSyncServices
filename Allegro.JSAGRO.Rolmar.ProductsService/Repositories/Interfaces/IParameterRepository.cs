using Allegro.JSAGRO.Rolmar.ProductsService.Models;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Repositories.Interfaces
{
    public interface IParameterRepository
    {
        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);

        Task UpdateParameter(int id, int parameterId, string value, CancellationToken ct);
    }
}