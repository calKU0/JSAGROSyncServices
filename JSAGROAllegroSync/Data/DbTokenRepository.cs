using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.Interfaces;
using JSAGROAllegroSync.Models;
using System.Data.Entity;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Data
{
    public class DbTokenRepository : ITokenRepository
    {
        private readonly MyDbContext _ctx;

        public DbTokenRepository(MyDbContext ctx) => _ctx = ctx;

        public async Task<TokenDto> GetTokensAsync()
        {
            var e = await _ctx.AllegroTokens.AsNoTracking().FirstOrDefaultAsync();
            if (e == null) return null;
            return new TokenDto
            {
                AccessToken = e.AccessToken,
                RefreshToken = e.RefreshToken,
                ExpiryDateUtc = e.ExpiryDateUtc
            };
        }

        public async Task SaveTokensAsync(TokenDto tokens)
        {
            var e = await _ctx.AllegroTokens.FirstOrDefaultAsync();
            if (e == null)
            {
                e = new AllegroTokenEntity();
                _ctx.AllegroTokens.Add(e);
            }
            e.AccessToken = tokens.AccessToken;
            e.RefreshToken = tokens.RefreshToken;
            e.ExpiryDateUtc = tokens.ExpiryDateUtc;
            await _ctx.SaveChangesAsync();
        }

        public async Task ClearAsync()
        {
            var e = await _ctx.AllegroTokens.FirstOrDefaultAsync();
            if (e != null)
            {
                _ctx.AllegroTokens.Remove(e);
                await _ctx.SaveChangesAsync();
            }
        }
    }
}