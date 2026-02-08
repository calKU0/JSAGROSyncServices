using Dapper;
using System.Data;
using JSAGROSyncServices.Shared.Data;
using JSAGROSyncServices.Shared.Interfaces;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Repositories
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
            using var connection = _context.CreateConnection();
            connection.Open();
            return await connection.ExecuteScalarAsync<int>(
                "AllegroImages_Add",
                new { ProductId = productId, Url = url },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task DeleteNotConnectedImages(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            await connection.ExecuteScalarAsync<int>(
                "AllegroImages_DeleteNotConnectedByProductId",
                new { ProductId = productId },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task MarkImagesAsConnectedAsync(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            await connection.ExecuteAsync(
                "AllegroImages_MarkConnectedByProductId",
                new { ProductId = productId },
                commandType: CommandType.StoredProcedure
            );
        }
    }
}