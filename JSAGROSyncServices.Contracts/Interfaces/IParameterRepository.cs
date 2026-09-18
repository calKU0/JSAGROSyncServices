using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IParameterRepository
    {
        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);
        /// <summary>Zwraca true, gdy parametr faktycznie zmienil wartosc.</summary>
        Task<bool> UpdateParameter(int id, int parameterId, string value, CancellationToken ct);

        /// <summary>
        /// Ustawia parametr po nazwie w kategorii produktu. Allegro w bledach podaje nazwe
        /// parametru, nie jego id. Zwraca true, gdy wartosc faktycznie sie zmienila.
        /// </summary>
        Task<bool> UpdateParameterByName(int productId, string parameterName, string value, CancellationToken ct);
        Task DeleteParameter(string categoryParameterName, int productId, CancellationToken ct);
    }
}