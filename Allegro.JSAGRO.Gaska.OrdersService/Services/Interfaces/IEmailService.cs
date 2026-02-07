namespace Allegro.JSAGRO.Gaska.OrdersService.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string htmlBody);
    }
}