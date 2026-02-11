using Dapper;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Shared.Data;
using Microsoft.Extensions.Options;
using System.Data;

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
                "AllegroTokens_GetByTokenName",
                new { TokenName = _credentials.ClientName },
                commandType: CommandType.StoredProcedure
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

            await conn.ExecuteAsync(
                "AllegroTokens_Upsert",
                new
                {
                    tokens.AccessToken,
                    tokens.RefreshToken,
                    tokens.ExpiryDateUtc,
                    TokenName = _credentials.ClientName
                },
                commandType: CommandType.StoredProcedure
            );
        }

        public async Task ClearAsync()
        {
            using var conn = _context.CreateConnection();
            await conn.ExecuteAsync(
                "AllegroTokens_DeleteByTokenName",
                new { TokenName = _credentials.ClientName },
                commandType: CommandType.StoredProcedure
            );
        }
    }
}