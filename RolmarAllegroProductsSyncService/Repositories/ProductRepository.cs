using Dapper;
using Microsoft.Extensions.Options;
using RolmarAllegroProductsSyncService.Data;
using RolmarAllegroProductsSyncService.DTOs;
using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Repositories.Interfaces;
using RolmarAllegroProductsSyncService.Settings;

namespace RolmarAllegroProductsSyncService.Repositories
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
            const string sql = @"SELECT * FROM RolmarProducts";

            using var connection = _context.CreateConnection();
            connection.Open();

            var products = await connection.QueryAsync<RolmarProduct>(sql, commandTimeout: 900);

            return products.ToList();
        }

        public async Task<List<RolmarProduct>> GetNotExistingProductsInAllegro(CancellationToken ct)
        {
            const string sql = @"SELECT * FROM RolmarProducts WHERE AllegroId is null";

            using var connection = _context.CreateConnection();
            connection.Open();

            var products = await connection.QueryAsync<RolmarProduct>(sql, commandTimeout: 900);

            return products.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct)
        {
            const string sql = @"SELECT
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.Ean,
                p.Weight,
                p.Fits,
                p.SupplierName,
                p.InStock,
                p.Unit,
                p.CurrencyPrice,
                p.PriceNet,
                p.PriceGross,
                p.DefaultAllegroCategory,
                p.Package,
                p.CreatedDate,
                p.UpdatedDate,

                ps.Id,
                ps.ProductId,
                ps.Name,
                ps.Value,
                ps.UnitName

            FROM RolmarProducts p
            LEFT JOIN ProductSpecifications ps ON ps.ProductId = p.Id
            WHERE NOT EXISTS (
                SELECT 1
                FROM RolmarProductParameters pp
                WHERE pp.ProductId = p.Id
            ) AND IntegrationCompany = 'Rolmar'
            ORDER BY p.Id;
            ";

            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                Models.ProductSpecification,
                RolmarProduct>(
                sql,
                (product, spec) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<Models.ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>();

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    return existing;
                },
                splitOn: "Id",
                commandTimeout: 900
            );

            return productDict.Values.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, CancellationToken ct)
        {
            const string sql = @"SELECT
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.Ean,
                p.Weight,
                p.Fits,
                p.SupplierName,
                p.InStock,
                p.Unit,
                p.CurrencyPrice,
                p.PriceNet,
                p.PriceGross,
                p.DefaultAllegroCategory,
                p.Package,
                p.CreatedDate,
                p.UpdatedDate,
                p.Substitutes,
                p.AllegroId,

                ps.Id,
                ps.ProductId,
                ps.Name,
                ps.Value,
                ps.UnitName,

                pp.Id,
                pp.ProductId,
                pp.CategoryParameterId,
                cp.Name,
                pp.Value,
                pp.IsForProduct

            FROM RolmarProducts p
            LEFT JOIN ProductSpecifications ps ON ps.ProductId = p.Id
            JOIN RolmarProductParameters pp ON pp.ProductId = p.Id
            JOIN CategoryParameters cp ON cp.Id = pp.CategoryParameterId
            LEFT JOIN AllegroOffers ao ON ao.ExternalId = p.Code
            WHERE p.InStock >= @MinProductStock AND NULLIF(p.DefaultAllegroCategory, 0) IS NOT NULL AND ao.Id IS NULL
            AND IntegrationCompany = 'Rolmar'
            ORDER BY p.Id;";

            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                Models.ProductSpecification,
                ProductParameter,
                RolmarProduct>(
                sql,
                (product, spec, param) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<Models.ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>();

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    if (param?.Id > 0 && !existing.Parameters.Any(p => p.Id == param.Id))
                        existing.Parameters.Add(param);

                    return existing;
                },
                new { MinProductStock = minProductStock },
                splitOn: "Id,Id",
                commandTimeout: 900
            );

            return productDict.Values.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsWithoutDefaultCategory(CancellationToken ct)
        {
            const string sql = @"SELECT
                p.Id,
                p.Code,
                p.Name,
                p.Description,
                p.Ean,
                p.Weight,
                p.Fits,
                p.SupplierName,
                p.InStock,
                p.Unit,
                p.CurrencyPrice,
                p.PriceNet,
                p.PriceGross,
                p.DefaultAllegroCategory,
                p.Package,
                p.CreatedDate,
                p.UpdatedDate,

                ps.Id,
                ps.ProductId,
                ps.Name,
                ps.Value,
                ps.UnitName

            FROM RolmarProducts p
            LEFT JOIN ProductSpecifications ps ON ps.ProductId = p.Id
            WHERE NULLIF(p.DefaultAllegroCategory, 0) IS NULL AND IntegrationCompany = 'Rolmar'
            ORDER BY p.Id;";

            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                Models.ProductSpecification,
                RolmarProduct>(
                sql,
                (product, spec) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<Models.ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>(); // puste

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    return existing;
                },
                splitOn: "Id",
                commandTimeout: 900
            );

            return productDict.Values.ToList();
        }

        public async Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct)
        {
            const string sql = @"
                UPDATE RolmarProducts
                SET DefaultAllegroCategory = @CategoryId,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Id = @ProductId;
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(sql, new
            {
                ProductId = productId,
                CategoryId = categoryId
            });
        }

        public async Task UpdateProductAllegroCategory(string productCode, string categoryId, CancellationToken ct)
        {
            const string sql = @"
                UPDATE RolmarProducts
                SET DefaultAllegroCategory = @CategoryId,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Code = @ProductCode;
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(sql, new
            {
                ProductCode = productCode,
                CategoryId = categoryId
            });
        }

        public async Task UpdateProductAllegroId(int productId, string allegroProductId, string allegroCategoryId, CancellationToken ct)
        {
            const string sql = @"UPDATE RolmarProducts SET AllegroId = @AllegroId, DefaultAllegroCategory = @CategoryId WHERE Id = @ProductId";

            using var connection = _context.CreateConnection();
            connection.Open();

            var products = await connection.QueryAsync(sql, new { AllegroId = allegroProductId, ProductId = productId, CategoryId = allegroCategoryId });
        }

        public async Task<bool> UpdateProductStockAsync(string productCode, int stock, CancellationToken ct)
        {
            const string sql = @"
                UPDATE RolmarProducts
                SET
                    InStock = @Stock,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Code = @ProductCode AND IntegrationCompany = 'Rolmar';
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            var affectedRows = await connection.ExecuteAsync(
                sql,
                new
                {
                    ProductCode = productCode,
                    Stock = stock
                }
            );

            return affectedRows > 0;
        }

        public async Task<bool> UpsertProductAsync(ProductResult product, CancellationToken ct)
        {
            const string upsertProductSql = @"
                MERGE RolmarProducts AS target
                USING (SELECT @Code AS Code) AS source
                ON target.Code = source.Code
                WHEN MATCHED THEN
                    UPDATE SET
                        Name = LEFT(@Name,
                            CASE
                                WHEN LEN(@Name) <= 75 THEN LEN(@Name)
                                ELSE 75 - CHARINDEX(' ', REVERSE(LEFT(@Name, 75))) + 1
                            END),
                        Description = @Description,
                        IntegrationCompany = 'Rolmar',
                        Ean = @Ean,
                        Weight = @Weight,
                        Fits = NULLIF(@Fits,''),
                        Substitutes = NULLIF(@Substitutes,''),
                        Unit = @Unit,
                        CurrencyPrice = @Currency,
                        PriceNet = @PriceNet,
                        PriceGross = @PriceGross,
                        Package = @Package,
                        UpdatedDate = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (Code, Name, Description, Ean, Weight, Fits, Substitutes, Unit, CurrencyPrice,
                            PriceNet, PriceGross, Package, CreatedDate, UpdatedDate, IntegrationCompany)
                    VALUES (@Code, @Name, @Description, @Ean, @Weight, NULLIF(@Fits,''), NULLIF(@Substitutes,''), @Unit, @Currency,
                            @PriceNet, @PriceGross, @Package, SYSUTCDATETIME(), SYSUTCDATETIME(), @IntegrationCompany)
                OUTPUT inserted.Id;
                ";

            const string deleteSpecsSql = @"
                DELETE FROM ProductSpecifications WHERE ProductId = @ProductId;
                ";

            const string insertSpecSql = @"
                INSERT INTO ProductSpecifications (ProductId, Name, Value, UnitName)
                VALUES (@ProductId, @Name, @Value, @UnitName);
                ";

            const string deleteCategoriesSql = @"
                DELETE FROM RolmarCategory WHERE ProductId = @ProductId;
                ";

            const string insertCategorySql = @"
                INSERT INTO RolmarCategory (ProductId, Name)
                VALUES (@ProductId, @Name);
                ";

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var productId = await connection.ExecuteScalarAsync<int>(
                    upsertProductSql,
                    new
                    {
                        Code = product.ProductIndex,
                        Name = product.Name,
                        Description = product.Description,
                        Ean = product.Ean,
                        Weight = float.TryParse(product.Weight, out var w) ? w : 0,
                        Fits = product.Fits,
                        Unit = product.Unit,
                        Currency = product.Currency,
                        Substitutes = product.Substitutes,
                        IntegrationCompany = "Rolmar",
                        PriceNet = decimal.TryParse(product.Price, out var pn) ? pn : 0,
                        PriceGross = decimal.TryParse(product.Price, out var pg) ? pg * 1.23m : 0,
                        Package = decimal.TryParse(product.ErpPackage, out var pkg) ? pkg : 0
                    },
                    transaction
                );

                // Replace specifications
                await connection.ExecuteAsync(
                    deleteSpecsSql,
                    new { ProductId = productId },
                    transaction
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
                        insertSpecSql,
                        specs,
                        transaction
                    );
                }

                // Replace categories
                await connection.ExecuteAsync(
                    deleteCategoriesSql,
                    new { ProductId = productId },
                    transaction
                );

                if (product.Categories?.Any() == true)
                {
                    var categories = product.Categories.Select(c => new
                    {
                        ProductId = productId,
                        Name = c
                    });

                    await connection.ExecuteAsync(
                        insertCategorySql,
                        categories,
                        transaction
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