using JSAGROSyncServices.Contracts.DTOs.Allegro;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Usuwa z patcha oferty zdjęcia zablokowane przez Allegro ("Zdjęcie zostało zablokowane") - i tylko je.
    ///
    /// Allegro sprawdza przy patchu galerię oferty, zdjęcia w opisie i zdjęcia produktu z katalogu, pod który
    /// oferta jest podpięta. Zdjęć produktu nie da się zmienić, ale w productSet można podać id produktu razem
    /// z jego listą zdjęć - wtedy oferta przechodzi walidację z tą listą, a produkt w katalogu zostaje bez zmian
    /// (sprawdzone na żywo 2026-11-26: samo id - 422, id + zdjęcia bez logo - 200).
    /// </summary>
    public static class BlockedImageFilter
    {
        /// <summary>
        /// Wycina zablokowane adresy z galerii, opisu i zdjęć produktu. Zdjęcia produktu z katalogu podajemy jawnie
        /// tylko wtedy, gdy znamy je i któreś jest zablokowane - inaczej zostaje samo id, jak dotąd.
        /// Zwraca liczbę usuniętych odwołań do zdjęć.
        /// </summary>
        public static int Apply(ProductOfferRequest request, IReadOnlySet<string> blocked, IReadOnlyDictionary<string, List<string>> catalogImages)
        {
            if (blocked.Count == 0)
                return 0;

            var removed = 0;

            if (request.Images != null)
            {
                var kept = request.Images.Where(url => !blocked.Contains(url)).ToList();
                removed += request.Images.Count - kept.Count;

                // Cała własna galeria zablokowana - galerią zostają czyste zdjęcia produktu z katalogu. Gdy i tych
                // nie ma, zostawiamy galerię bez zmian: pustej Allegro nie przyjmie, a błąd pokaże problem.
                if (kept.Count == 0)
                    kept = CleanCatalogImages(request, blocked, catalogImages);

                if (kept.Count > 0)
                    request.Images = kept;
            }

            if (request.Description?.Sections != null)
            {
                foreach (var section in request.Description.Sections)
                {
                    var before = section.SectionItems.Count;
                    section.SectionItems = section.SectionItems
                        .Where(item => !(string.Equals(item.Type, "IMAGE", StringComparison.OrdinalIgnoreCase) && item.Url != null && blocked.Contains(item.Url)))
                        .ToList();
                    removed += before - section.SectionItems.Count;
                }

                request.Description.Sections = request.Description.Sections.Where(s => s.SectionItems.Count > 0).ToList();
            }

            foreach (var product in (request.ProductSet ?? new List<ProductSet>()).Select(s => s.ProductObject).Where(p => p != null))
            {
                if (!string.IsNullOrWhiteSpace(product!.Id))
                {
                    if (catalogImages.TryGetValue(product.Id, out var images) && images.Any(blocked.Contains))
                    {
                        var kept = images.Where(url => !blocked.Contains(url)).ToList();

                        // Wszystkie zdjęcia produktu zablokowane (np. same logo czy banery marki) - podajemy czyste
                        // zdjęcia z galerii oferty. Allegro przyjmuje je obok id produktu (sprawdzone na żywo 2026-11-26).
                        if (kept.Count == 0 && request.Images != null)
                            kept = request.Images.Where(url => !blocked.Contains(url)).ToList();

                        if (kept.Count > 0)
                        {
                            product.Images = kept;
                            removed += images.Count(blocked.Contains);
                        }
                    }
                }
                else if (product.Images != null)
                {
                    var kept = product.Images.Where(url => !blocked.Contains(url)).ToList();
                    removed += product.Images.Count - kept.Count;

                    if (kept.Count > 0)
                        product.Images = kept;
                }
            }

            return removed;
        }

        /// <summary>Niezablokowane zdjęcia pierwszego produktu z katalogu w productSet, o ile je znamy.</summary>
        private static List<string> CleanCatalogImages(ProductOfferRequest request, IReadOnlySet<string> blocked, IReadOnlyDictionary<string, List<string>> catalogImages)
        {
            var productId = (request.ProductSet ?? new List<ProductSet>())
                .Select(s => s.ProductObject?.Id)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id) && catalogImages.ContainsKey(id!));

            return productId == null
                ? new List<string>()
                : catalogImages[productId].Where(url => !blocked.Contains(url)).ToList();
        }

        /// <summary>
        /// Zdjęcia, z których któreś może być zablokowane - do szukania po jednym. Najpierw zdjęcia produktu
        /// z katalogu, potem własne; w każdej grupie od końca (doklejane grafiki trafiały na koniec galerii).
        /// </summary>
        public static List<string> Candidates(ProductOfferRequest request, IReadOnlyDictionary<string, List<string>> catalogImages, IReadOnlySet<string> blocked)
        {
            var catalog = (request.ProductSet ?? new List<ProductSet>())
                .Select(s => s.ProductObject)
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id) && catalogImages.ContainsKey(p.Id!))
                .SelectMany(p => catalogImages[p!.Id!].AsEnumerable().Reverse());

            var own = (request.Images ?? new List<string>()).AsEnumerable().Reverse()
                .Concat((request.Description?.Sections ?? new List<Section>())
                    .SelectMany(s => s.SectionItems)
                    .Where(i => string.Equals(i.Type, "IMAGE", StringComparison.OrdinalIgnoreCase) && i.Url != null)
                    .Select(i => i.Url!));

            return catalog.Concat(own)
                .Where(url => !string.IsNullOrWhiteSpace(url) && !blocked.Contains(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
