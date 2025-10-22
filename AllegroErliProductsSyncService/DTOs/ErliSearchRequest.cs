namespace AllegroErliProductsSyncService.DTOs
{
    public class ErliSearchRequest
    {
        public Pagination Pagination { get; set; }
        public List<string> Fields { get; set; }
    }

    public class Pagination
    {
        public string SortField { get; set; }
        public string After { get; set; }
        public string Order { get; set; }
        public int Limit { get; set; }
    }

    public class ErliProduct
    {
        public string ExternalId { get; set; }
    }

    public class ErliSearchResponse
    {
        public List<ErliProduct> Items { get; set; }
        public string NextAfter { get; set; }
    }
}