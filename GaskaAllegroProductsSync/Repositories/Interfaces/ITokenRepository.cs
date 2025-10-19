using GaskaAllegroProductsSync.DTOs;

namespace GaskaAllegroProductsSync.Repositories.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}