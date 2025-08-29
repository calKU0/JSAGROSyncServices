namespace JSAGROAllegroServiceConfiguration.Models
{
    public class LogFileItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public int WarningsCount { get; set; }
        public int ErrorsCount { get; set; }
    }
}