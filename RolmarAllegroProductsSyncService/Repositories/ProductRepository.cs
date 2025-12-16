using Dapper;
using RolmarAllegroProductsSyncService.Data;
using RolmarAllegroProductsSyncService.DTOs;
using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Repositories.Interfaces;

namespace RolmarAllegroProductsSyncService.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly DapperContext _context;

        public ProductRepository(DapperContext dbContext)
        {
            _context = dbContext;
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
            )
            ORDER BY p.Id;
            ";

            using var connection = _context.CreateConnection();

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
                splitOn: "Id"
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

                ps.Id,
                ps.ProductId,
                ps.Name,
                ps.Value,
                ps.UnitName,

                pp.Id,
                pp.ProductId,
                pp.CategoryParameterId,
                pp.Value,
                pp.IsForProduct

            FROM RolmarProducts p
            LEFT JOIN ProductSpecifications ps ON ps.ProductId = p.Id
            JOIN RolmarProductParameters pp ON pp.ProductId = p.Id
            WHERE p.InStock >= @MinProductStock
              AND p.DefaultAllegroCategory IS NOT NULL
              AND p.DefaultAllegroCategory <> 0
            ORDER BY p.Id;";

            using var connection = _context.CreateConnection();

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
                splitOn: "Id,Id"
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
            WHERE p.DefaultAllegroCategory IS NULL
               OR p.DefaultAllegroCategory = 0
            ORDER BY p.Id;";

            using var connection = _context.CreateConnection();

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
                splitOn: "Id"
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

            var affectedRows = await connection.ExecuteAsync(sql, new
            {
                ProductId = productId,
                CategoryId = categoryId
            });

            if (affectedRows == 0)
                throw new InvalidOperationException($"Product with Id {productId} not found.");
        }

        public async Task UpdateProductAllegroCategory(string productId, string categoryId, CancellationToken ct)
        {
            const string sql = @"
                UPDATE RolmarProducts
                SET DefaultAllegroCategory = @CategoryId,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Code = @ProductId;
                ";

            using var connection = _context.CreateConnection();

            var affectedRows = await connection.ExecuteAsync(sql, new
            {
                ProductId = productId,
                CategoryId = categoryId
            });

            if (affectedRows == 0)
                throw new InvalidOperationException($"Product with Id {productId} not found.");
        }

        public async Task<bool> UpdateProductStockAsync(string productCode, int stock, CancellationToken ct)
        {
            const string sql = @"
                UPDATE RolmarProducts
                SET
                    InStock = @Stock,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Code = @ProductCode;
                ";

            using var connection = _context.CreateConnection();

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
                        Name = @Name,
                        Description = @Description,
                        Ean = @Ean,
                        Weight = @Weight,
                        Fits = @Fits,
                        Unit = @Unit,
                        CurrencyPrice = @Currency,
                        PriceNet = @PriceNet,
                        PriceGross = @PriceGross,
                        Package = @Package,
                        UpdatedDate = SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (Code, Name, Description, Ean, Weight, Fits, Unit, CurrencyPrice,
                            PriceNet, PriceGross, Package, CreatedDate, UpdatedDate)
                    VALUES (@Code, @Name, @Description, @Ean, @Weight, @Fits, @Unit, @Currency,
                            @PriceNet, @PriceGross, @Package, SYSUTCDATETIME(), SYSUTCDATETIME())
                OUTPUT inserted.Id;
                ";

            const string deleteSpecsSql = @"
                DELETE FROM ProductSpecifications WHERE ProductId = @ProductId;
                ";

            const string insertSpecSql = @"
                INSERT INTO ProductSpecifications (ProductId, Name, Value, UnitName)
                VALUES (@ProductId, @Name, @Value, @UnitName);
                ";

            using var connection = _context.CreateConnection();
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
                        PriceNet = decimal.TryParse(product.Price, out var pn) ? pn : 0,
                        PriceGross = decimal.TryParse(product.RetailPrice, out var pg) ? pg : 0,
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