using Dapper;
using JSAGROSyncServices.Shared.DTOs.Allegro;
using JSAGROSyncServices.Shared.Models;
using RolmarAllegroProductsSyncService.Data;
using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Repositories.Interfaces;

namespace RolmarAllegroProductsSyncService.Repositories
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

            AllegroCategory parentEntity = null;

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            while (stack.Any())
            {
                var dto = stack.Pop();

                var existing = await conn.QueryFirstOrDefaultAsync<AllegroCategory>(
                    "SELECT * FROM AllegroCategories WHERE CategoryId = @CategoryId",
                    new { CategoryId = dto.Id }, tran
                );

                if (existing == null)
                {
                    var id = await conn.ExecuteScalarAsync<int>(
                        @"INSERT INTO AllegroCategories (CategoryId, Name, ParentId)
                          VALUES (@CategoryId, @Name, @ParentId); SELECT SCOPE_IDENTITY();",
                        new { CategoryId = dto.Id, Name = dto.Name, ParentId = parentEntity?.Id }, tran
                    );
                    parentEntity = new AllegroCategory { Id = id, CategoryId = dto.Id, Name = dto.Name, ParentId = parentEntity?.Id };
                }
                else
                {
                    await conn.ExecuteAsync(
                        "UPDATE AllegroCategories SET Name = @Name, ParentId = @ParentId WHERE Id = @Id",
                        new { Name = dto.Name, ParentId = parentEntity?.Id, Id = existing.Id }, tran
                    );
                    parentEntity = existing;
                }
            }

            tran.Commit();
        }

        public async Task<IEnumerable<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct = default)
        {
            const string sql = @"
                SELECT
                    cp.*,
                    cpv.Id AS ValueId, cpv.Value, cpv.CategoryParameterId
                FROM CategoryParameters cp
                LEFT JOIN CategoryParameterValues cpv ON cp.Id = cpv.CategoryParameterId
                WHERE cp.CategoryId = @CategoryId";

            using var conn = _context.CreateConnection();

            var paramDict = new Dictionary<int, CategoryParameter>();

            await conn.QueryAsync<CategoryParameter, CategoryParameterValue, CategoryParameter>(
                sql,
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
                new { CategoryId = categoryId },
                splitOn: "ValueId"
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

            var paramList = parameters.ToList();

            // Get existing category parameters
            var categoryIds = paramList.Select(p => p.CategoryId).Distinct();
            var paramIds = paramList.Select(p => p.ParameterId).Distinct();

            var existing = (await conn.QueryAsync<CategoryParameter>(
                @"SELECT * FROM CategoryParameters
                    WHERE CategoryId IN @CategoryIds AND ParameterId IN @ParamIds",
                new { CategoryIds = categoryIds, ParamIds = paramIds }, tran
            )).ToDictionary(p => (p.CategoryId, p.ParameterId));

            var toInsert = new List<CategoryParameter>();
            var toUpdate = new List<CategoryParameter>();

            foreach (var param in paramList)
            {
                if (existing.TryGetValue((param.CategoryId, param.ParameterId), out var existingParam))
                {
                    existingParam.Name = param.Name;
                    existingParam.Type = param.Type;
                    existingParam.Required = param.Required;
                    existingParam.RequiredForProduct = param.RequiredForProduct;
                    existingParam.DescribesProduct = param.DescribesProduct;
                    existingParam.CustomValuesEnabled = param.CustomValuesEnabled;
                    existingParam.AmbiguousValueId = param.AmbiguousValueId;
                    existingParam.Min = param.Min;
                    existingParam.Max = param.Max;

                    toUpdate.Add(existingParam);
                }
                else
                {
                    toInsert.Add(param);
                }
            }

            // 1. Insert new CategoryParameters
            if (toInsert.Any())
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO CategoryParameters
                    (CategoryId, ParameterId, Name, Type, Required, Min, Max, RequiredForProduct, DescribesProduct, CustomValuesEnabled, AmbiguousValueId)
                    VALUES (@CategoryId, @ParameterId, @Name, @Type, @Required, @Min, @Max, @RequiredForProduct, @DescribesProduct, @CustomValuesEnabled, @AmbiguousValueId)",
                    toInsert, tran);
            }

            // 2. Update existing CategoryParameters
            if (toUpdate.Any())
            {
                await conn.ExecuteAsync(@"
                    UPDATE CategoryParameters
                    SET Name = @Name, Type = @Type, Required = @Required, Min = @Min, Max = @Max, RequiredForProduct = @RequiredForProduct,
                        DescribesProduct = @DescribesProduct, CustomValuesEnabled = @CustomValuesEnabled, AmbiguousValueId = @AmbiguousValueId
                    WHERE CategoryId = @CategoryId AND ParameterId = @ParameterId",
                    toUpdate, tran);
            }

            // 3. Handle parameter values
            var allParams = toInsert.Concat(toUpdate).ToList();
            if (allParams.Any())
            {
                var allIds = await conn.QueryAsync<int>(
                    @"SELECT Id FROM CategoryParameters
                        WHERE CategoryId IN @CategoryIds AND ParameterId IN @ParamIds",
                    new { CategoryIds = categoryIds, ParamIds = paramIds }, tran);

                var idDict = (await conn.QueryAsync<(int Id, int CategoryId, int ParameterId)>(
                    @"SELECT Id, CategoryId, ParameterId FROM CategoryParameters
                        WHERE CategoryId IN @CategoryIds AND ParameterId IN @ParamIds",
                    new { CategoryIds = categoryIds, ParamIds = paramIds }, tran))
                    .ToDictionary(x => (x.CategoryId, x.ParameterId), x => x.Id);

                // Delete existing values
                await conn.ExecuteAsync(@"
                    DELETE FROM CategoryParameterValues
                    WHERE CategoryParameterId IN @Ids",
                    new { Ids = idDict.Values }, tran);

                // Insert new values
                var newValues = allParams
                    .Where(p => p.Values != null && p.Values.Any())
                    .SelectMany(p => p.Values.Select(v => new
                    {
                        CategoryParameterId = idDict[(p.CategoryId, p.ParameterId)],
                        v.Value
                    }))
                    .ToList();

                if (newValues.Any())
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO CategoryParameterValues (CategoryParameterId, Value)
                        VALUES (@CategoryParameterId, @Value)",
                        newValues, tran);
                }
            }

            tran.Commit();
        }

        public async Task<IEnumerable<int>> GetDefaultCategories(CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            var result = await conn.QueryAsync<int>(
                @"SELECT DISTINCT p.DefaultAllegroCategory
                  FROM RolmarProducts p
                  LEFT JOIN CategoryParameters cp ON cp.CategoryId = p.DefaultAllegroCategory
                  WHERE p.DefaultAllegroCategory != 0 AND cp.CategoryId IS NULL"
            );
            return result;
        }

        public async Task<IEnumerable<AllegroCategory>> GetAllegroCategories(CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            return await conn.QueryAsync<AllegroCategory>("SELECT * FROM AllegroCategories");
        }
    }
}