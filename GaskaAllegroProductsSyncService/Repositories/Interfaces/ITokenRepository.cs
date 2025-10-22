using AllegroGaskaProductsSyncService.DTOs;

namespace AllegroGaskaProductsSyncService.Repositories.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}