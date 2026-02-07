namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface IAllegroProductService
    {
        Task SearchProducts(CancellationToken ct = default);
    }
}