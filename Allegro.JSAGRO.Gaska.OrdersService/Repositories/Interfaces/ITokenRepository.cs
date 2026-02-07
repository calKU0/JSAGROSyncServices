using Allegro.JSAGRO.Gaska.OrdersService.DTOs;

namespace Allegro.JSAGRO.Gaska.OrdersService.Repositories.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}