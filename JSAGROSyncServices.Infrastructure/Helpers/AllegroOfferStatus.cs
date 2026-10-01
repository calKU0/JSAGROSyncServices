namespace JSAGROSyncServices.Infrastructure.Helpers
{
    /// <summary>
    /// Statusy publikacji oferty w Allegro. Rozróżnienie jest istotne przy kończeniu ofert:
    /// komenda END zmienia tylko ofertę faktycznie opublikowaną. Dla oferty, która nigdy nie
    /// została wystawiona (INACTIVE) albo jest już zakończona (ENDED), komenda przechodzi bez
    /// błędu i nie zmienia statusu - oferta wracała więc do kończenia w każdym cyklu i jedna
    /// usługa wysyłała z tego powodu ponad tysiąc zbędnych żądań dziennie.
    /// </summary>
    public static class AllegroOfferStatus
    {
        /// <summary>Oferta opublikowana i widoczna dla kupujących.</summary>
        public const string Active = "ACTIVE";

        /// <summary>Publikacja w toku.</summary>
        public const string Activating = "ACTIVATING";

        /// <summary>Oferta nigdy nie została wystawiona albo została wycofana bez zakończenia.</summary>
        public const string Inactive = "INACTIVE";

        /// <summary>Oferta zakończona.</summary>
        public const string Ended = "ENDED";

        /// <summary>
        /// Czy oferta jest opublikowana, czyli czy komenda END ma co zakończyć. ACTIVATING liczymy
        /// jako opublikowaną - publikacja jest w toku i zakończenie ją zatrzyma.
        /// </summary>
        public static bool IsPublished(string? status) =>
            Is(status, Active) || Is(status, Activating);

        /// <summary>Czy oferta jest już zakończona.</summary>
        public static bool IsEnded(string? status) => Is(status, Ended);

        private static bool Is(string? status, string expected) =>
            string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
