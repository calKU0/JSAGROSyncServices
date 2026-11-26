using JSAGROSyncServices.Products.Configuration;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;

namespace JSAGROSyncServices.Products.Repositories
{
    public class ImageRepository : IImageRepository
    {
        private readonly ServiceContext _service;
        private readonly DapperContext _context;

        public ImageRepository(DapperContext context, ServiceContext serviceContext)
        {
            _service = serviceContext;
            _context = context;
        }

        public async Task<int> AddImageAsync(int productId, string url, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            return await connection.ExecuteScalarAsync<int>(
                "AllegroImages_Add",
                new { ProductId = productId, Url = url, Account = _service.Account },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<List<AllegroImages>> GetProductImagesAsync(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            var rows = await connection.QueryAsync<AllegroImages>(
                new CommandDefinition(
                    @"SELECT Id, ProductId, Url, Connected
                      FROM dbo.AllegroImages
                      WHERE ProductId = @ProductId AND Account = @Account
                      ORDER BY Id;",
                    new { ProductId = productId, Account = _service.Account },
                    cancellationToken: ct));

            return rows.ToList();
        }

        public async Task DeleteNotConnectedImages(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            await connection.ExecuteScalarAsync<int>(
                "AllegroImages_DeleteNotConnectedByProductId",
                new { ProductId = productId, Account = _service.Account },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<int> DeleteProductImagesAsync(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            return await connection.ExecuteScalarAsync<int>(
                "AllegroImages_DeleteByProductId",
                new { ProductId = productId, Account = _service.Account },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<int> DeleteProductImagesForAllAccountsAsync(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            // NULL w procedurze znaczy "wszystkie konta".
            return await connection.ExecuteScalarAsync<int>(
                "AllegroImages_DeleteByProductId",
                new { ProductId = productId, Account = (int?)null },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task<HashSet<string>> GetBlockedImageUrlsAsync(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            var urls = await connection.QueryAsync<string>(
                new CommandDefinition("AllegroBlockedImages_GetAll", commandType: CommandType.StoredProcedure, cancellationToken: ct));

            return new HashSet<string>(urls, StringComparer.OrdinalIgnoreCase);
        }

        public async Task AddBlockedImageUrlAsync(string url, string? reason, string? offerId, string? code, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(
                new CommandDefinition("AllegroBlockedImages_Add",
                    new { Url = url, Reason = reason, OfferId = offerId, Code = code },
                    commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }

        public async Task<Dictionary<string, List<string>>> GetCatalogProductImagesAsync(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            var rows = await connection.QueryAsync<(string CatalogProductId, string Images)>(
                new CommandDefinition("AllegroCatalogProductImages_GetAll", commandType: CommandType.StoredProcedure, cancellationToken: ct));

            return rows.ToDictionary(
                r => r.CatalogProductId,
                r => System.Text.Json.JsonSerializer.Deserialize<List<string>>(r.Images) ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task SaveCatalogProductImagesAsync(string catalogProductId, IReadOnlyCollection<string> images, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(
                new CommandDefinition("AllegroCatalogProductImages_Upsert",
                    new { CatalogProductId = catalogProductId, Images = System.Text.Json.JsonSerializer.Serialize(images) },
                    commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }

        public async Task MarkImagesAsConnectedAsync(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            await connection.ExecuteAsync(
                "AllegroImages_MarkConnectedByProductId",
                new { ProductId = productId, Account = _service.Account },
                commandType: CommandType.StoredProcedure
            );
        }
    }
}