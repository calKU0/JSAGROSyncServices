namespace JSAGROSyncServices.Contracts.Models
{
    /// <summary>
    /// Oferta, której serwis nie umie naprawić przez API (np. zablokowane zdjęcie w produkcie z katalogu
    /// i brak wariantu). Trzymana między cyklami do podsumowania na końcu każdego cyklu.
    /// </summary>
    public sealed record UnfixableOffer(string OfferId, string Code, string? CatalogProductId, string Reason, DateTime FirstSeenAt, DateTime LastSeenAt);
}
