namespace ServiceManager.Models
{
    /// <summary>
    /// Kategoria dostawcy do wyboru w konfiguratorze.
    /// <see cref="Key"/> trafia do appsettings (Gąska: id, Rolmar: ścieżka), <see cref="Path"/> widzi użytkownik.
    /// </summary>
    public sealed record CategoryOption(string Key, string Path, int Depth)
    {
        /// <summary>Tekst do wyszukiwania - bez polskich znaków i wielkich liter.</summary>
        public string SearchText { get; } = TolerantSearch.Normalize($"{Path} {Key}");
    }
}
