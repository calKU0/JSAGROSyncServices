namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface IImageRespository
    {
        Task<int> AddImageAsync(int productId, string url, CancellationToken ct);

        Task MarkImagesAsConnectedAsync(int productId, CancellationToken ct);

        Task DeleteNotConnectedImages(int productId, CancellationToken ct);
    }
}