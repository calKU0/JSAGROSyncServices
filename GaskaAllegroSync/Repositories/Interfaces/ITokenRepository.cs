using GaskaAllegroSync.DTOs;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Repositories.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}