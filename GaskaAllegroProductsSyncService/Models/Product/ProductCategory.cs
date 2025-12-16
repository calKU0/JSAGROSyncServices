namespace GaskaAllegroProductsSyncService.Models
{
    public class ProductCategory
    {
        public int Id { get; set; }

        public int CategoryId { get; set; }
        public int ParentID { get; set; }
        public string Name { get; set; }

        public int ProductId { get; set; }
    }
}