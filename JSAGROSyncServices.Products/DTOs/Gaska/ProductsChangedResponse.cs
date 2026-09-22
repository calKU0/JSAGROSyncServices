namespace JSAGROSyncServices.Products.DTOs.Gaska
{
    public class ProductsChangedReponse
    {
        public List<ProductChanged> Products { get; set; } = new();
    }
    public class ProductChanged
    {
        public int TwrId { get; set; }
        public string? CodeGaska { get; set; }
    }
}
