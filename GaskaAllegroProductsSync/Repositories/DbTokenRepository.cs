using Dapper;
using GaskaAllegroProductsSync.Data;
using GaskaAllegroProductsSync.DTOs;
using GaskaAllegroProductsSync.Models;
using GaskaAllegroProductsSync.Repositories.Interfaces;

namespace GaskaAllegroProductsSync.Repositories
{
    public class DbTokenRepository : ITokenRepository
    {
        private readonly DapperContext _context;

        public DbTokenRepository(DapperContext context) => _context = context;

        public async Task<TokenDto> GetTokensAsync()
        {
            using var conn = _context.CreateConnection();
            var entity = await conn.QueryFirstOrDefaultAsync<AllegroTokenEntity>(
                "SELECT TOP 1 * FROM AllegroTokenEntities"
            );

            if (entity == null) return null;

            return new TokenDto
            {
                AccessToken = entity.AccessToken,
                RefreshToken = entity.RefreshToken,
                ExpiryDateUtc = entity.ExpiryDateUtc
            };
        }

        public async Task SaveTokensAsync(TokenDto tokens)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            var existing = await conn.QueryFirstOrDefaultAsync<AllegroTokenEntity>(
                "SELECT TOP 1 * FROM AllegroTokenEntities"
            );

            if (existing == null)
            {
                const string insertSql = @"
                    INSERT INTO AllegroTokenEntities (AccessToken, RefreshToken, ExpiryDateUtc)
                    VALUES (@AccessToken, @RefreshToken, @ExpiryDateUtc);";

                await conn.ExecuteAsync(insertSql, new
                {
                    tokens.AccessToken,
                    tokens.RefreshToken,
                    tokens.ExpiryDateUtc
                });
            }
            else
            {
                const string updateSql = @"
                    UPDATE AllegroTokenEntities
                    SET AccessToken = @AccessToken,
                        RefreshToken = @RefreshToken,
                        ExpiryDateUtc = @ExpiryDateUtc
                    WHERE Id = @Id;";

                await conn.ExecuteAsync(updateSql, new
                {
                    tokens.AccessToken,
                    tokens.RefreshToken,
                    tokens.ExpiryDateUtc,
                    existing.Id
                });
            }
        }

        public async Task ClearAsync()
        {
            using var conn = _context.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM AllegroTokenEntities");
        }
    }
}