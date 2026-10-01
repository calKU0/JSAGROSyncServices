using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IParameterRepository
    {
        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);

        /// <summary>
        /// Uzupełnia parametry, których wartość nie zależy od danych produktu: liczbę sztuk
        /// w ofercie i stronę zabudowy. Dotyczy także produktów, które mają już inne parametry -
        /// te nie wracają do zwykłego kroku przypisywania parametrów. Zwraca liczbę dodanych wpisów.
        /// </summary>
        /// <param name="countInOfferSqlPattern">Wzorzec LIKE nazw parametrów "Liczba ... w ofercie".</param>
        /// <param name="countInOfferValue">Wartość wstawiana dla tych parametrów.</param>
        /// <param name="mountingSideName">Nazwa parametru "Strona zabudowy".</param>
        /// <param name="universalMountingSides">Dopuszczalne wartości "nie zawęża zastosowania", w kolejności preferencji.</param>
        /// <param name="ct">Token anulowania.</param>
        Task<int> FillDataIndependentParametersAsync(
            string countInOfferSqlPattern,
            string countInOfferValue,
            string mountingSideName,
            IReadOnlyList<string> universalMountingSides,
            CancellationToken ct);
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