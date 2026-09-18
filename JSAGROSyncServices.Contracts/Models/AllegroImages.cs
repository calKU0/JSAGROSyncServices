using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroImages
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string Url { get; set; } = string.Empty;
        public AllegroAccount Account { get; set; }
        public bool Connected { get; set; } = false;
    }
}