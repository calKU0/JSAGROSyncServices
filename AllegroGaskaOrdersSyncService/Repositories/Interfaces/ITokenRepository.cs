using AllegroGaskaOrdersSyncService.DTOs;

namespace AllegroGaskaOrdersSyncService.Repositories.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}