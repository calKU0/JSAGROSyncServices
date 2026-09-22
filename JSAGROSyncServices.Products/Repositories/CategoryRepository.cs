using Dapper;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;
using System.Text.Json;

namespace JSAGROSyncServices.Products.Repositories
{
    public class CategoryRepository : ICategoryRepository
    {
        private readonly DapperContext _context;

        public CategoryRepository(DapperContext context)
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

            AllegroCategory? parentEntity = null;

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            while (stack.Any())
            {
                var dto = stack.Pop();

                var id = await conn.ExecuteScalarAsync<int>(
                    "AllegroCategories_Upsert",
                    new { CategoryId = dto.Id, Name = dto.Name, ParentId = parentEntity?.Id },
                    tran,
                    commandType: CommandType.StoredProcedure
                );

                parentEntity = new AllegroCategory { Id = id, CategoryId = dto.Id ?? string.Empty, Name = dto.Name ?? string.Empty, ParentId = parentEntity?.Id };
            }

            tran.Commit();
        }

        public async Task<IEnumerable<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct = default)
        {
            using var conn = _context.CreateConnection();

            var paramDict = new Dictionary<int, CategoryParameter>();

            await conn.QueryAsync<CategoryParameter, CategoryParameterValue, CategoryParameter>(
                "CategoryParameters_GetByCategoryId",
                (cp, cpv) =>
                {
                    if (!paramDict.TryGetValue(cp.Id, out var categoryParam))
                    {
                        categoryParam = cp;
                        categoryParam.Values = new List<CategoryParameterValue>();
                        paramDict.Add(cp.Id, categoryParam);
                    }

                    if (cpv != null && !string.IsNullOrEmpty(cpv.Value))
                        categoryParam.Values.Add(cpv);

                    return categoryParam;
                },
                new { CategoryId = categoryId, OnlyForOffers = 0 },
                splitOn: "ValueId",
                commandType: CommandType.StoredProcedure
            );

            return paramDict.Values;
        }

        public async Task SaveCategoryParametersAsync(IEnumerable<CategoryParameter> parameters, CancellationToken ct)
        {
            if (parameters == null || !parameters.Any())
                return;

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            foreach (var param in parameters)
            {
                var valuesJson = param.Values != null && param.Values.Any()
                    ? JsonSerializer.Serialize(param.Values.Select(v => v.Value))
                    : null;

                await conn.ExecuteAsync(
                    "CategoryParameters_UpsertWithValues",
                    new
                    {
                        param.CategoryId,
                        param.ParameterId,
                        param.Name,
                        param.Type,
                        param.Required,
                        param.Min,
                        param.Max,
                        param.RequiredForProduct,
                        param.DescribesProduct,
                        param.CustomValuesEnabled,
                        param.AmbiguousValueId,
                        ValuesJson = valuesJson
                    },
                    tran,
                    commandType: CommandType.StoredProcedure
                );
            }

            tran.Commit();
        }

        public async Task<IEnumerable<int>> GetDefaultCategories(CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            var result = await conn.QueryAsync<int>(
                "RolmarProducts_GetDefaultAllegroCategoriesWithoutParameters",
                commandType: CommandType.StoredProcedure);
            return result;
        }

        /// <summary>
        /// Kategoria Allegro najczęściej używana przez inne produkty z tej samej kategorii dostawcy.
        /// Podpowiedź dla produktów, dla których Allegro nie zaproponowało kategorii.
        /// </summary>
        public async Task<int?> GetMostCommonDefaultAllegroCategory(int productId, CancellationToken ct)
        {
            using var conn = _context.CreateConnection();

            var productCategories = (await conn.QueryAsync<(int Id, string Path)>(
                new CommandDefinition(
                    @"SELECT sc.Id, sc.Path
                      FROM dbo.ProductCategories pc
                      JOIN dbo.SupplierCategories sc ON sc.Id = pc.CategoryId
                      WHERE pc.ProductId = @ProductId",
                    new { ProductId = productId },
                    cancellationToken: ct))).ToList();

            if (productCategories.Count == 0)
                return null;

            // Gąska ma równoległe drzewa ("wg rodzaju", "wg producentów") - rodzaj części lepiej wskazuje kategorię Allegro.
            var source = productCategories.FirstOrDefault(c => c.Path.Contains("Części według rodzaju", StringComparison.OrdinalIgnoreCase));

            if (source == default)
                source = productCategories[0];

            var mostCommon = await conn.QueryFirstOrDefaultAsync<int?>(
                new CommandDefinition(
                    @"SELECT TOP 1 p.DefaultAllegroCategory
                      FROM dbo.ProductCategories pc
                      JOIN dbo.RolmarProducts p ON p.Id = pc.ProductId
                      WHERE pc.CategoryId = @CategoryId
                        AND pc.ProductId <> @ProductId
                        AND p.DefaultAllegroCategory <> 0
                      GROUP BY p.DefaultAllegroCategory
                      ORDER BY COUNT(*) DESC",
                    new { CategoryId = source.Id, ProductId = productId },
                    cancellationToken: ct));

            if (mostCommon is > 0)
                return mostCommon;

            var path = source.Path.ToLowerInvariant();

            if (path.Contains("traktor")) return 305829;
            if (path.Contains("kombajn")) return 319159;

            return null;
        }

        public async Task<IEnumerable<AllegroCategory>> GetAllegroCategories(CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            return await conn.QueryAsync<AllegroCategory>(
                "AllegroCategories_GetAll",
                commandType: CommandType.StoredProcedure);
        }
    }
}