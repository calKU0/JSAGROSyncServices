namespace Allegro.JSAGRO.Gaska.ProductsService.Models.Product
{
    public class ProductParameter
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; }
        public bool IsForProduct { get; set; }

        public virtual CategoryParameter CategoryParameter { get; set; }
    }
}