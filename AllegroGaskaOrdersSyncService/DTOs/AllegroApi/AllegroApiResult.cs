namespace AllegroGaskaOrdersSyncService.DTOs.AllegroApi
{
    public class ApiResult<T>
    {
        public T? Data { get; set; }
        public HttpResponseMessage Response { get; set; } = default!;
        public string Body { get; set; } = string.Empty;
    }
}