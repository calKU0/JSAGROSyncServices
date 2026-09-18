namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroCategory
    {
        public int Id { get; set; }

        public string CategoryId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AllegroCategory? Parent { get; set; }

        public int? ParentId { get; set; }

        public ICollection<AllegroCategory> Children { get; set; } = new List<AllegroCategory>();
    }
}