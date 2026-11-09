namespace JSAGROSyncServices.Contracts.Models
{
    /// <summary>Skrót treści ostatniego udanego pełnego patcha oferty Allegro i czas jego wysłania (UTC).</summary>
    public sealed record OfferContentState(string ContentHash, DateTime SentAt);
}
