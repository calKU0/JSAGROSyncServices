namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroOfferService
    {
        Task SyncAllegroOffers(CancellationToken ct = default);

        Task SyncAllegroOffersDetails(CancellationToken ct = default);

        Task CreateOffers(CancellationToken ct = default);

        Task UpdateOffers(CancellationToken ct = default);

        /// <summary>Wypisuje na końcu cyklu oferty, których nie da się naprawić przez API (także z poprzednich cykli).</summary>
        Task LogUnfixableSummary(CancellationToken ct = default);
    }
}