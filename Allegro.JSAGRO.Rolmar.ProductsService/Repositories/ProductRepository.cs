using Allegro.JSAGRO.Rolmar.ProductsService.Constants;
using Allegro.JSAGRO.Rolmar.ProductsService.Settings;
using Dapper;
using JSAGROSyncServices.Shared.Data;
using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Models;
using Microsoft.Extensions.Options;
using System.Data;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly DapperContext _context;
        private readonly List<Settings.Delivery> _deliveries;
        public ProductRepository(DapperContext dbContext, IOptions<AppSettings> options)
        {
            _context = dbContext;
            _deliveries = options.Value.Deliveries;
        }

        public async Task<List<RolmarProduct>> GetAllProducts(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            var products = await connection.QueryAsync<RolmarProduct>(
                "RolmarProducts_GetAll",
                new { IntegrationCompany = ServiceConstants.Company },
                commandTimeout: 900,
                commandType: CommandType.StoredProcedure);

            return products.ToList();
        }

        public async Task<List<RolmarProduct>> GetNotExistingProductsInAllegro(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            var products = await connection.QueryAsync<RolmarProduct>(
                "RolmarProducts_GetWithoutAllegroId",
                new { IntegrationCompany = ServiceConstants.Company },
                commandTimeout: 900,
                commandType: CommandType.StoredProcedure);

            return products.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                JSAGROSyncServices.Shared.Models.ProductSpecification,
                RolmarProduct>(
                "RolmarProducts_GetToUpdateParameters",
                (product, spec) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<JSAGROSyncServices.Shared.Models.ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>();

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    return existing;
                },
                new { IntegrationCompany = ServiceConstants.Company },
                splitOn: "Id",
                commandTimeout: 900,
                commandType: CommandType.StoredProcedure
            );

            return productDict.Values.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                JSAGROSyncServices.Shared.Models.ProductSpecification,
                ProductParameter,
                RolmarProduct>(
                "RolmarProducts_GetToUpload",
                (product, spec, param) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<JSAGROSyncServices.Shared.Models.ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>();

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    if (param?.Id > 0 && !existing.Parameters.Any(p => p.Id == param.Id))
                        existing.Parameters.Add(param);

                    return existing;
                },
                new { MinProductStock = minProductStock, IntegrationCompany = ServiceConstants.Company, Account = ServiceConstants.Account },
                splitOn: "Id,Id",
                commandTimeout: 900,
                commandType: CommandType.StoredProcedure
            );

            return productDict.Values.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsWithoutDefaultCategory(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                JSAGROSyncServices.Shared.Models.ProductSpecification,
                RolmarProduct>(
                "RolmarProducts_GetWithoutDefaultCategory",
                (product, spec) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<JSAGROSyncServices.Shared.Models.ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>(); // puste

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    return existing;
                },
                new { IntegrationCompany = ServiceConstants.Company },
                splitOn: "Id",
                commandTimeout: 900,
                commandType: CommandType.StoredProcedure
            );

            return productDict.Values.ToList();
        }

        public async Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(
                "RolmarProducts_UpdateDefaultCategoryById",
                new
                {
                    ProductId = productId,
                    CategoryId = categoryId
                },
                commandType: CommandType.StoredProcedure);
        }

        public async Task UpdateProductAllegroCategory(string productCode, string categoryId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(
                "RolmarProducts_UpdateDefaultCategoryByCode",
                new
                {
                    ProductCode = productCode,
                    CategoryId = categoryId
                },
                commandType: CommandType.StoredProcedure);
        }

        public async Task UpdateProductAllegroId(int productId, string allegroProductId, string allegroCategoryId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteAsync(
                "RolmarProducts_UpdateAllegroId",
                new { AllegroId = allegroProductId, ProductId = productId, CategoryId = allegroCategoryId },
                commandType: CommandType.StoredProcedure);
        }

        public async Task<bool> UpdateProductStockAsync(string productCode, int stock, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(
                "RolmarProducts_UpdateStockByCode",
                new
                {
                    ProductCode = productCode,
                    Stock = stock
                },
                commandType: CommandType.StoredProcedure
            );

            return affectedRows > 0;
        }

        public async Task<bool> UpsertProductAsync(RolmarProduct product, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var productId = await connection.ExecuteScalarAsync<int>(
                    "RolmarProducts_Upsert",
                    new
                    {
                        Code = product.Code,
                        Name = product.Name,
                        Description = product.Description,
                        Ean = product.Ean,
                        Weight = product.Weight,
                        Fits = product.Fits,
                        Unit = product.Unit,
                        Currency = product.CurrencyPrice,
                        Substitutes = product.Substitutes,
                        IntegrationCompany = ServiceConstants.Company,
                        PriceNet = product.PriceNet,
                        PriceGross = product.PriceGross,
                        Package = product.Package
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure
                );

                // Replace specifications
                await connection.ExecuteAsync(
                    "ProductSpecifications_DeleteByProductId",
                    new { ProductId = productId },
                    transaction,
                    commandType: CommandType.StoredProcedure
                );

                if (product.Specifications?.Any() == true)
                {
                    var specs = product.Specifications.Select(s => new
                    {
                        ProductId = productId,
                        s.Name,
                        s.Value,
                        s.UnitName
                    });

                    await connection.ExecuteAsync(
                        "ProductSpecifications_Insert",
                        specs,
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );
                }

                // Replace categories
                await connection.ExecuteAsync(
                    "RolmarCategory_DeleteByProductId",
                    new { ProductId = productId },
                    transaction,
                    commandType: CommandType.StoredProcedure
                );

                if (product.Categories?.Any() == true)
                {
                    var categories = product.Categories.Select(c => new
                    {
                        ProductId = productId,
                        Name = c.Name
                    });

                    await connection.ExecuteAsync(
                        "RolmarCategory_Insert",
                        categories,
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}