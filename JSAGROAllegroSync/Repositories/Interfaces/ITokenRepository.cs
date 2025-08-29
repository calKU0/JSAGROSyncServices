using JSAGROAllegroSync.DTOs;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}