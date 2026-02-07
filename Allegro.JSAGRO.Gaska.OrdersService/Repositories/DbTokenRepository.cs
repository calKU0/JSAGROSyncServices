using Allegro.JSAGRO.Gaska.OrdersService.Data;
using Allegro.JSAGRO.Gaska.OrdersService.DTOs;
using Allegro.JSAGRO.Gaska.OrdersService.Models;
using Allegro.JSAGRO.Gaska.OrdersService.Repositories.Interfaces;
using Allegro.JSAGRO.Gaska.OrdersService.Settings;
using Dapper;
using Microsoft.Extensions.Options;

namespace Allegro.JSAGRO.Gaska.OrdersService.Repositories
{
    public class DbTokenRepository : ITokenRepository
    {
        private readonly DapperContext _context;
        private readonly AllegroApiCredentials _credentials;

        public DbTokenRepository(DapperContext context, IOptions<AllegroApiCredentials> options)
        {
            _context = context;
            _credentials = options.Value;
        }

        public async Task<TokenDto?> GetTokensAsync()
        {
            using var conn = _context.CreateConnection();

            var entity = await conn.QueryFirstOrDefaultAsync<AllegroTokenEntity>(
                "SELECT TOP 1 * FROM AllegroTokenEntities WHERE TokenName = @TokenName",
                new { TokenName = _credentials.ClientName }
            );

            if (entity == null)
                return null;

            return new TokenDto
            {
                AccessToken = entity.AccessToken,
                RefreshToken = entity.RefreshToken,
                ExpiryDateUtc = entity.ExpiryDateUtc,
                TokenName = entity.TokenName
            };
        }

        public async Task SaveTokensAsync(TokenDto tokens)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            var existing = await conn.QueryFirstOrDefaultAsync<AllegroTokenEntity>(
                "SELECT TOP 1 * FROM AllegroTokenEntities WHERE TokenName = @TokenName",
                new { TokenName = _credentials.ClientName }
            );

            if (existing == null)
            {
                const string insertSql = @"
                    INSERT INTO AllegroTokenEntities (AccessToken, RefreshToken, ExpiryDateUtc, TokenName)
                    VALUES (@AccessToken, @RefreshToken, @ExpiryDateUtc, @TokenName);";

                await conn.ExecuteAsync(insertSql, new
                {
                    tokens.AccessToken,
                    tokens.RefreshToken,
                    tokens.ExpiryDateUtc,
                    TokenName = _credentials.ClientName
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
            await conn.ExecuteAsync(
                "DELETE FROM AllegroTokenEntities WHERE TokenName = @TokenName",
                new { TokenName = _credentials.ClientName }
            );
        }
    }
}