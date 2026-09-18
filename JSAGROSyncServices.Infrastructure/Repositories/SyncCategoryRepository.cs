using Dapper;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace JSAGROSyncServices.Infrastructure.Repositories
{
    public class SyncCategoryRepository : ISyncCategoryRepository
    {
        private readonly DapperContext _context;
        private readonly AllegroAccount _account;
        private readonly IntegrationCompany _company;

        public SyncCategoryRepository(DapperContext context, AllegroAccount account, IntegrationCompany company)
        {
            _context = context;
            _account = account;
            _company = company;
        }

        public async Task ReplaceAccountCategoriesAsync(IEnumerable<string> categories, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("Category", typeof(string));

            foreach (var category in categories ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;

                table.Rows.Add(category.Trim());
            }

            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "SyncCategories_ReplaceByAccount",
                    new
                    {
                        Account = _account,
                        IntegrationCompany = _company,
                        Categories = table.AsTableValuedParameter("dbo.SyncCategoryType")
                    },
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: ct));
        }

        public async Task<List<string>> GetCompanyCategoriesAsync(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            var categories = await connection.QueryAsync<string>(
                new CommandDefinition(
                    "SyncCategories_GetByCompany",
                    new { IntegrationCompany = _company },
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: ct));

            return categories
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList();
        }

        public async Task ReplaceProductCategoriesAsync(
            IReadOnlyDictionary<string, HashSet<int>> categoryIdsByProductCode,
            IReadOnlyCollection<int> fetchedCategoryIds,
            CancellationToken ct)
        {
            if (categoryIdsByProductCode == null || categoryIdsByProductCode.Count == 0)
                return;

            if (fetchedCategoryIds == null || fetchedCategoryIds.Count == 0)
                return;

            var table = new DataTable();
            table.Columns.Add("Code", typeof(string));
            table.Columns.Add("CategoryId", typeof(int));

            foreach (var (code, categoryIds) in categoryIdsByProductCode)
            {
                if (string.IsNullOrWhiteSpace(code))
                    continue;

                foreach (var categoryId in categoryIds)
                {
                    table.Rows.Add(code, categoryId);
                }
            }

            if (table.Rows.Count == 0)
                return;

            var fetched = new DataTable();
            fetched.Columns.Add("CategoryId", typeof(int));

            foreach (var categoryId in fetchedCategoryIds)
            {
                fetched.Rows.Add(categoryId);
            }

            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "ProductSupplierCategories_ReplaceByCodes",
                    new
                    {
                        IntegrationCompany = _company,
                        Items = table.AsTableValuedParameter("dbo.ProductSupplierCategoryType"),
                        FetchedCategories = fetched.AsTableValuedParameter("dbo.SupplierCategoryIdType")
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
        }
    }
}
