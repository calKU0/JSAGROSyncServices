namespace AllegroGaskaProductsSyncService.Models.Product
{
    public class RecommendedPart
    {
        public int Id { get; set; }

        public int TwrID { get; set; }
        public string CodeGaska { get; set; }
        public float Qty { get; set; }

        public int ProductId { get; set; }
    }
}