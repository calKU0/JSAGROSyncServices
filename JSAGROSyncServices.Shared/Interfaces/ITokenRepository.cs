using JSAGROSyncServices.Shared.DTOs.Allegro;

namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}