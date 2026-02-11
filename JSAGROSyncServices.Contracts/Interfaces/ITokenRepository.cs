using JSAGROSyncServices.Contracts.DTOs.Allegro;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface ITokenRepository
    {
        Task<TokenDto?> GetTokensAsync();

        Task SaveTokensAsync(TokenDto tokens);

        Task ClearAsync();
    }
}