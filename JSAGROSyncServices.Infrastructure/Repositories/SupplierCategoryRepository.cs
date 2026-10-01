using Dapper;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace JSAGROSyncServices.Infrastructure.Repositories
{
    public class SupplierCategoryRepository : ISupplierCategoryRepository
    {
        // Drzewa kategorii maja po kilka tysiecy wezlow, a przypisania - dziesiatki tysiecy wierszy.
        private const int LinkBatchSize = 5000;

        private readonly DapperContext _context;
        private readonly IntegrationCompany _company;

        public SupplierCategoryRepository(DapperContext context, IntegrationCompany company)
        {
            _context = context;
            _company = company;
        }

        public async Task UpsertNodesAsync(IEnumerable<SupplierCategoryNode> nodes, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("SourceKey", typeof(string));
            table.Columns.Add("ParentSourceKey", typeof(string));
            table.Columns.Add("Name", typeof(string));

            // Klucz jest kluczem glownym typu tabelarycznego - duplikat wywrocilby cale wywolanie.
            foreach (var node in nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.SourceKey) && !string.IsNullOrWhiteSpace(n.Name))
                .DistinctBy(n => n.SourceKey, StringComparer.OrdinalIgnoreCase))
            {
                table.Rows.Add(node.SourceKey, (object?)node.ParentSourceKey ?? DBNull.Value, node.Name);
            }

            if (table.Rows.Count == 0)
                return;

            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "SupplierCategories_Upsert",
                    new
                    {
                        IntegrationCompany = _company,
                        Nodes = table.AsTableValuedParameter("dbo.SupplierCategoryNodeType")
                    },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
        }

        public async Task<int> CountNodesAsync(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            return await connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM dbo.SupplierCategories WHERE IntegrationCompany = @IntegrationCompany",
                    new { IntegrationCompany = _company },
                    cancellationToken: ct));
        }

        public async Task ReplaceProductCategoriesAsync(IReadOnlyDictionary<string, List<string>> categoryKeysByProductCode, CancellationToken ct)
        {
            if (categoryKeysByProductCode.Count == 0)
                return;

            using var connection = _context.CreateConnection();
            connection.Open();

            foreach (var batch in categoryKeysByProductCode.Chunk(LinkBatchSize))
            {
                var codes = new DataTable();
                codes.Columns.Add("Code", typeof(string));

                var links = new DataTable();
                links.Columns.Add("Code", typeof(string));
                links.Columns.Add("CategorySourceKey", typeof(string));

                foreach (var (code, keys) in batch)
                {
                    if (string.IsNullOrWhiteSpace(code))
                        continue;

                    codes.Rows.Add(code);

                    foreach (var key in keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase))
                        links.Rows.Add(code, key);
                }

                if (codes.Rows.Count == 0)
                    continue;

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "ProductCategories_ReplaceByCodes",
                        new
                        {
                            IntegrationCompany = _company,
                            Codes = codes.AsTableValuedParameter("dbo.ProductCodeType"),
                            Links = links.AsTableValuedParameter("dbo.ProductCategoryLinkType")
                        },
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 900,
                        cancellationToken: ct));
            }
        }
    }
}
