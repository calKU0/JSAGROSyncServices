namespace AllegroGaskaProductsSyncService.Models.Product
{
    public class Component
    {
        public int Id { get; set; }

        public int TwrID { get; set; }
        public string CodeGaska { get; set; }
        public float Qty { get; set; }

        public int ProductId { get; set; }
    }
}