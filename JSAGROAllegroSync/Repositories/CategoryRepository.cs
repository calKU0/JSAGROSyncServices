using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly MyDbContext _context;

        public CategoryRepository(MyDbContext context)
        {
            _context = context;
        }

        public async Task SaveCategoryTreeAsync(CategoryDto category, CancellationToken ct)
        {
            var stack = new Stack<CategoryDto>();
            var current = category;

            while (current != null)
            {
                stack.Push(current);
                current = current.Parent;
            }

            AllegroCategory parentEntity = null;

            while (stack.Any())
            {
                var dto = stack.Pop();

                var existing = await _context.AllegroCategories.FirstOrDefaultAsync(c => c.CategoryId == dto.Id, ct);

                if (existing == null)
                {
                    existing = new AllegroCategory { CategoryId = dto.Id, Name = dto.Name, Parent = parentEntity };
                    _context.AllegroCategories.Add(existing);
                }
                else
                {
                    existing.Name = dto.Name;
                    if (parentEntity != null)
                        existing.Parent = parentEntity;
                }

                await _context.SaveChangesAsync(ct);
                parentEntity = existing;
            }
        }

        public async Task<List<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct = default)
        {
            return await _context.CategoryParameters
                .Where(cp => cp.CategoryId == categoryId)
                .ToListAsync(ct);
        }

        public async Task SaveCategoryParametersAsync(IEnumerable<CategoryParameter> parameters, CancellationToken ct)
        {
            if (parameters == null || !parameters.Any())
                return;

            const int batchSize = 500;
            var paramList = parameters.ToList();

            // Batch processing to avoid huge queries
            var batches = paramList
                .Select((p, index) => new { p, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.p).ToList());

            foreach (var batch in batches)
            {
                var categoryIds = batch.Select(p => p.CategoryId).Distinct().ToList();
                var paramIds = batch.Select(p => p.ParameterId).Distinct().ToList();

                // Fetch existing parameters for this batch in one query
                var existingParams = await _context.CategoryParameters
                    .Include(cp => cp.Values)
                    .Where(cp => categoryIds.Contains(cp.CategoryId) && paramIds.Contains(cp.ParameterId))
                    .ToListAsync(ct);

                var existingDict = existingParams.ToDictionary(
                    p => (p.CategoryId, p.ParameterId)
                );

                var toInsert = new List<CategoryParameter>();
                var toRemoveValues = new List<CategoryParameterValue>();

                foreach (var param in batch)
                {
                    if (existingDict.TryGetValue((param.CategoryId, param.ParameterId), out var existing))
                    {
                        existing.Name = param.Name;
                        existing.Type = param.Type;
                        existing.Required = param.Required;
                        existing.Min = param.Min;
                        existing.Max = param.Max;

                        if (existing.Values != null && existing.Values.Any())
                            _context.CategoryParameterValues.RemoveRange(existing.Values);

                        existing.Values = param.Values ?? new List<CategoryParameterValue>();
                    }
                    else
                    {
                        toInsert.Add(param);
                    }
                }

                if (toInsert.Any())
                    _context.CategoryParameters.AddRange(toInsert);

                await _context.SaveChangesAsync(ct);
            }
        }

        public async Task<List<int>> GetDefaultCategories(CancellationToken ct)
        {
            // 1. Get products with DefaultAllegroCategory and without parameters
            var productsData = await _context.Products
                .Where(p => p.DefaultAllegroCategory != 0
                            && !p.Archived
                            && !_context.CategoryParameters.Any(cp => cp.CategoryId == p.DefaultAllegroCategory))
                .Select(p => new
                {
                    p.DefaultAllegroCategory
                })
                .Distinct()
                .ToListAsync(ct);

            var products = productsData.Select(p => p.DefaultAllegroCategory).ToList();

            return products;
        }

        public async Task<int?> GetMostCommonDefaultAllegroCategory(int productId, CancellationToken ct)
        {
            // 1 Load all categories for this product
            var productCategories = await _context.ProductCategories
                .Where(pc => pc.ProductId == productId)
                .ToListAsync(ct);

            if (!productCategories.Any())
                return null;

            // 2 Find root branches
            var rootBranches = productCategories.Where(pc => pc.ParentID == 0).ToList();

            // Prefer root 19178 ("Części według rodzaju")
            var mainRoot = rootBranches.FirstOrDefault(r => r.CategoryId == 19178)
                           ?? rootBranches.FirstOrDefault();

            if (mainRoot == null)
                return null;

            // 3 Traverse branch to get all categories in this branch
            var branchCategories = productCategories
                .Where(pc => IsInBranch(pc, mainRoot.CategoryId, productCategories))
                .ToList();

            // Leaf = category that is not a ParentID for any other in this branch
            var leaf = branchCategories.FirstOrDefault(c => !branchCategories.Any(other => other.ParentID == c.CategoryId));

            if (leaf != null)
            {
                // 4 Find other products with the same leaf and non-null DefaultAllegroCategory
                var categoryStats = await _context.ProductCategories
                    .Where(pc => pc.CategoryId == leaf.CategoryId
                                 && pc.ProductId != productId
                                 && pc.Product.DefaultAllegroCategory != 0)
                    .GroupBy(pc => pc.Product.DefaultAllegroCategory)
                    .Select(g => new
                    {
                        CategoryId = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Count)
                    .FirstOrDefaultAsync(ct);

                if (categoryStats != null)
                    return categoryStats.CategoryId;
            }

            // 5 Fallback: search branch names for 'traktor'
            var traktorMatch = branchCategories.FirstOrDefault(c => c.Name.ToLower().Contains("traktor"));
            if (traktorMatch != null)
            {
                // return Motoryzacja/Części do maszyn i innych pojazdów/Części do maszyn rolniczych/Do traktorów/Części montażowe
                return 305829;
            }

            // 6 Fallback: search branch names for 'kombajn'
            var kombajnMatch = branchCategories.FirstOrDefault(c => c.Name.ToLower().Contains("kombajn"));
            if (kombajnMatch != null)
            {
                // return Motoryzacja/Części do maszyn i innych pojazdów/Części do maszyn rolniczych/Do kombajnów/Części montażowe
                return 319159;
            }

            return null;
        }

        public async Task<List<AllegroCategory>> GetAllegroCategories(CancellationToken ct)
        {
            return await _context.AllegroCategories.ToListAsync(ct);
        }

        public async Task<List<CompatibleProduct>> GetCompatibilityList(CancellationToken ct)
        {
            return await _context.CompatibleProducts.ToListAsync(ct);
        }

        private bool IsInBranch(ProductCategory category, int rootId, List<ProductCategory> allCategories)
        {
            if (category.CategoryId == rootId)
                return true;

            var parent = allCategories.FirstOrDefault(c => c.CategoryId == category.ParentID);
            if (parent == null)
                return false;

            return IsInBranch(parent, rootId, allCategories);
        }
    }
}