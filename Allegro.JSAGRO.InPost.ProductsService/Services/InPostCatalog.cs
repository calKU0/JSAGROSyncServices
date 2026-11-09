using Allegro.JSAGRO.InPost.ProductsService.DTOs;

namespace Allegro.JSAGRO.InPost.ProductsService.Services
{
    /// <summary>
    /// Drzewo kategorii InPost i definicje atrybutów - pobierane raz na cykl i trzymane w pamięci,
    /// bo przy kilku tysiącach ofert w czterech kategoriach to te same kilka zapytań.
    /// </summary>
    public class InPostCatalog
    {
        private readonly InPostClient _client;
        private readonly List<InPostCategory> _roots = new();
        private readonly Dictionary<string, InPostCategory> _categories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<AttributeDefinition>> _attributes = new(StringComparer.OrdinalIgnoreCase);

        public InPostCatalog(InPostClient client)
        {
            _client = client;
        }

        public async Task LoadAsync(CancellationToken ct)
        {
            _roots.Clear();
            _categories.Clear();
            _paths.Clear();
            _attributes.Clear();

            var tree = await _client.GetAsync<List<InPostCategory>>("v1/categories?depth=4", ct);

            void Walk(InPostCategory node, List<string> path)
            {
                var current = new List<string>(path) { node.Name };
                _categories[node.Id] = node;
                _paths[node.Id] = current;

                foreach (var child in node.Children ?? new List<InPostCategory>())
                    Walk(child, current);
            }

            _roots.AddRange(tree);

            foreach (var root in tree)
                Walk(root, new List<string>());
        }

        public IReadOnlyList<InPostCategory> Roots => _roots;

        public InPostCategory? Find(string id) => _categories.TryGetValue(id, out var category) ? category : null;

        /// <summary>Kategoria po UUID albo po ścieżce nazw ("Dom i ogród > Narzędzia > Pozostałe narzędzia").</summary>
        public InPostCategory? Resolve(string idOrPath)
        {
            if (Find(idOrPath) is { } byId)
                return byId;

            var key = Mappers.InPostOfferBuilder.Normalize(idOrPath.Replace(">", " "));

            var match = _paths.FirstOrDefault(p => Mappers.InPostOfferBuilder.Normalize(string.Join(" ", p.Value)) == key);

            return match.Key == null ? null : Find(match.Key);
        }

        public string PathOf(string id) => _paths.TryGetValue(id, out var path) ? string.Join(" > ", path) : id;

        public async Task<IReadOnlyList<AttributeDefinition>> GetAttributesAsync(string categoryId, CancellationToken ct)
        {
            if (_attributes.TryGetValue(categoryId, out var cached))
                return cached;

            var definitions = await _client.GetAsync<List<AttributeDefinition>>($"v1/categories/{categoryId}/attributes", ct);
            _attributes[categoryId] = definitions;

            return definitions;
        }
    }
}
