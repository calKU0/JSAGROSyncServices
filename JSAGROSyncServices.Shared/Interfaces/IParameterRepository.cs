using JSAGROSyncServices.Shared.Models;

namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface IParameterRepository
    {
        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);

        Task UpdateParameter(int id, int parameterId, string value, CancellationToken ct);
    }
}