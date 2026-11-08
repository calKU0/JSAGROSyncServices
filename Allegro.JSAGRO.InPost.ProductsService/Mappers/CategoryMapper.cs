using Allegro.JSAGRO.InPost.ProductsService.DTOs;
using Allegro.JSAGRO.InPost.ProductsService.Models;
using Allegro.JSAGRO.InPost.ProductsService.Settings;

namespace Allegro.JSAGRO.InPost.ProductsService.Mappers
{
    /// <summary>
    /// Kategoria InPost dla oferty Allegro.
    ///
    /// 1. Ręczne mapowanie z konfiguracji (kategoria albo jej najbliższy przodek). Wartością może być
    ///    UUID kategorii InPost albo jej ścieżka ("Dom i ogród > Narzędzia > Pozostałe narzędzia").
    /// 2. Dopasowanie po ścieżce nazw: drzewo InPost powtarza nazwy Allegro, więc schodzimy poziom
    ///    po poziomie, dopóki nazwy się zgadzają. Gałęzie nazwane inaczej (Allegro "Motoryzacja > Chemia",
    ///    InPost "Motoryzacja > Chemia samochodowa") wyrównują aliasy z konfiguracji.
    /// 3. InPost bywa płytszy - gdy zgodne nazwy skończą się na kategorii-rodzicu, bierzemy jej liść
    ///    "Pozostałe..." (także w postaci "Części do pozostałych maszyn i pojazdów").
    ///
    /// Wyszukiwania samej nazwy w całym drzewie celowo nie ma: "Klucze" czy "Podkładki" występują
    /// w InPost w zupełnie innych działach (klucze do gitar, podkładki pod mysz).
    /// </summary>
    public class CategoryMapper
    {
        private readonly Dictionary<int, AllegroCategoryNode> _allegro;
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
        private readonly List<(string[] From, string[] To)> _aliases;
        private readonly List<InPostCategory> _inPostRoots;
        private readonly Dictionary<int, CategoryMatch> _cache = new();

        public CategoryMapper(
            IEnumerable<AllegroCategoryNode> allegroCategories,
            IReadOnlyDictionary<string, string> map,
            IEnumerable<InPostCategory> inPostRoots,
            IEnumerable<CategoryPathAlias>? aliases = null)
        {
            _allegro = allegroCategories
                .GroupBy(c => c.CategoryId)
                .ToDictionary(g => g.Key, g => g.First());
            _inPostRoots = inPostRoots.ToList();

            _aliases = (aliases ?? Enumerable.Empty<CategoryPathAlias>())
                .Where(a => !string.IsNullOrWhiteSpace(a.From) && !string.IsNullOrWhiteSpace(a.To))
                .Select(a => (Split(a.From), Split(a.To)))
                .OrderByDescending(a => a.Item1.Length)
                .ToList();

            // Wartości mapy będące ścieżką zamieniamy na UUID raz, przy starcie.
            var byPath = new Dictionary<string, string>(StringComparer.Ordinal);

            void Index(InPostCategory node, string path)
            {
                var current = path.Length == 0 ? InPostOfferBuilder.Normalize(node.Name) : path + ">" + InPostOfferBuilder.Normalize(node.Name);
                byPath[current] = node.Id;

                foreach (var child in node.Children ?? new List<InPostCategory>())
                    Index(child, current);
            }

            foreach (var root in _inPostRoots)
                Index(root, string.Empty);

            foreach (var (allegroId, target) in map)
            {
                var key = string.Join(">", Split(target));
                _map[allegroId.Trim()] = byPath.TryGetValue(key, out var id) ? id : target.Trim();
            }
        }

        public CategoryMatch Map(int allegroCategoryId)
        {
            if (!_cache.TryGetValue(allegroCategoryId, out var match))
                _cache[allegroCategoryId] = match = Resolve(allegroCategoryId);

            return match;
        }

        private CategoryMatch Resolve(int allegroCategoryId)
        {
            var chain = Chain(allegroCategoryId);

            foreach (var id in new[] { allegroCategoryId }.Concat(chain.Select(c => c.CategoryId)).Distinct())
            {
                if (_map.TryGetValue(id.ToString(), out var mapped))
                    return new CategoryMatch(mapped, null);
            }

            if (chain.Count == 0)
                return new CategoryMatch(null, $"Brak nazwy kategorii Allegro {allegroCategoryId} w bazie (krok 'Allegro category paths' serwisu JSAGRO-Gąska jeszcze jej nie zapisał).");

            var originalNames = chain.AsEnumerable().Reverse().Select(c => c.Name).ToList();
            var names = ApplyAliases(originalNames.Select(InPostOfferBuilder.Normalize).ToList());

            InPostCategory? node = null;
            IEnumerable<InPostCategory> level = _inPostRoots;

            foreach (var key in names)
            {
                var next = level.FirstOrDefault(c => InPostOfferBuilder.Normalize(c.Name) == key);

                if (next == null)
                    break;

                node = next;

                if (node.Leaf)
                    break;

                level = node.Children ?? new List<InPostCategory>();
            }

            var allegroPath = string.Join(" > ", originalNames);

            if (node == null)
                return new CategoryMatch(null, $"Brak odpowiednika kategorii Allegro '{allegroPath}' w InPost.");

            if (node.Leaf)
                return new CategoryMatch(node.Id, null);

            var other = (node.Children ?? new List<InPostCategory>())
                .FirstOrDefault(c => c.Leaf && InPostOfferBuilder.Normalize(c.Name).Contains("pozosta", StringComparison.Ordinal));

            return other != null
                ? new CategoryMatch(other.Id, null)
                : new CategoryMatch(null, $"Kategoria Allegro '{allegroPath}' kończy się w InPost na '{node.Name}', która nie jest liściem i nie ma 'Pozostałe'.");
        }

        /// <summary>Podmienia początek ścieżki Allegro według najdłuższego pasującego aliasu.</summary>
        private List<string> ApplyAliases(List<string> names)
        {
            foreach (var (from, to) in _aliases)
            {
                if (from.Length <= names.Count && from.SequenceEqual(names.Take(from.Length)))
                    return to.Concat(names.Skip(from.Length)).ToList();
            }

            return names;
        }

        private static string[] Split(string path) =>
            path.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(InPostOfferBuilder.Normalize)
                .ToArray();

        /// <summary>Kategoria i jej przodkowie, od kategorii do korzenia.</summary>
        private List<AllegroCategoryNode> Chain(int categoryId)
        {
            var chain = new List<AllegroCategoryNode>();
            var seen = new HashSet<int>();
            int? current = categoryId;

            while (current is { } id && seen.Add(id) && _allegro.TryGetValue(id, out var node))
            {
                chain.Add(node);
                current = node.ParentId;
            }

            return chain;
        }
    }

    /// <summary>Wynik mapowania: id kategorii InPost albo powód, dla którego go nie ma.</summary>
    public record CategoryMatch(string? InPostCategoryId, string? Reason);
}
