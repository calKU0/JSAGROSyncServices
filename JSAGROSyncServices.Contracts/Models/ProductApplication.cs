namespace JSAGROSyncServices.Contracts.Models
{
    public class ProductApplication
    {
        public int Id { get; set; }

        public int ApplicationId { get; set; }
        public int ParentID { get; set; }
        public string Name { get; set; } = string.Empty;

        public int ProductId { get; set; }
    }
}