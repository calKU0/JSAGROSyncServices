namespace JSAGROSyncServices.Contracts.Models;

public class ProductPackage
{
    public int Id { get; set; }

    public string PackUnit { get; set; } = string.Empty;
    public float PackQty { get; set; }
    public float PackNettWeight { get; set; }
    public float PackGrossWeight { get; set; }
    public string PackEan { get; set; } = string.Empty;
    public int PackRequired { get; set; }

    public int ProductId { get; set; }
}
