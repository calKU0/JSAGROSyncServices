using Dapper;
using JSAGROSyncServices.Shared.Interfaces;
using RolmarAllegroProductsSyncService.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace RolmarAllegroProductsSyncService.Repositories
{
    public class ImageRepository : IImageRespository
    {
        private readonly DapperContext _context;

        public ImageRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<int> AddImageAsync(int productId, string url, CancellationToken ct)
        {
            const string sql = @"
                DELETE FROM dbo.AllegroImages
                WHERE ProductId = @ProductId
                  AND Url = @Url;

                INSERT INTO dbo.AllegroImages (ProductId, Url, Connected)
                VALUES (@ProductId, @Url, 0);

                SELECT CAST(SCOPE_IDENTITY() AS INT);
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            return await connection.ExecuteScalarAsync<int>(
                sql,
                new { ProductId = productId, Url = url }
            );
        }

        public async Task DeleteNotConnectedImages(int productId, CancellationToken ct)
        {
            const string sql = @"
                DELETE FROM dbo.AllegroImages
                WHERE ProductId = @ProductId
                  AND Connected = 0;
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            await connection.ExecuteScalarAsync<int>(
                sql,
                new { ProductId = productId }
            );
        }

        public async Task MarkImagesAsConnectedAsync(int productId, CancellationToken ct)
        {
            const string sql = @"
                UPDATE dbo.AllegroImages
                SET Connected = 1
                WHERE ProductId = @ProductId";

            using var connection = _context.CreateConnection();
            connection.Open();
            await connection.ExecuteAsync(
                sql,
                new { ProductId = productId }
            );
        }
    }
}