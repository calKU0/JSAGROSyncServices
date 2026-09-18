namespace JSAGROSyncServices.Contracts.Models
{
    public class CategoryParameterValue
    {
        public int Id { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; } = string.Empty;
        public virtual CategoryParameter? Parameter { get; set; }
    }
}