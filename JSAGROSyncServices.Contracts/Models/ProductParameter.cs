namespace JSAGROSyncServices.Contracts.Models
{
    public class ProductParameter
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; } = string.Empty;
        public bool IsForProduct { get; set; }
    }
}