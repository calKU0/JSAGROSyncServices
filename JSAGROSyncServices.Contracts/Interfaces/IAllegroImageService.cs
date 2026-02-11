namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroImageService
    {
        Task ImportImages(CancellationToken ct = default);
    }
}