using JSAGROSyncServices.Products.Configuration;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
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