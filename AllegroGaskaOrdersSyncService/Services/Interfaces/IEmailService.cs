namespace AllegroGaskaOrdersSyncService.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string htmlBody);
    }
}