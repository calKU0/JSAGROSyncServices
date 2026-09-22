namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroTokenEntity
    {
        public int Id { get; set; }

        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string TokenName { get; set; } = string.Empty;
        public DateTime ExpiryDateUtc { get; set; }
    }
}