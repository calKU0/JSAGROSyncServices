using Allegro.JSAGRO.Rolmar.ProductsService.Constants;
using Allegro.JSAGRO.Rolmar.ProductsService.Settings;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Data;
using Microsoft.Extensions.Options;
using System.Data;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly DapperContext _context;
        private readonly List<DeliverySettings> _deliveries;
        public ProductRepository(DapperContext dbContext, IOptions<AppSettings> options)
        {
            _context = dbContext;
            _deliveries = options.Value.Deliveries;
        }

        public async Task UpsertProductsBatchAsync(List<RolmarProduct> products, CancellationToken ct)
        {
            if (products == null || products.Count == 0)
                return;

            using var connection = _context.CreateConnection();
            connection.Open();

            var table = new DataTable();
            table.Columns.Add("Code", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("SupplierLogo", typeof(string));
            table.Columns.Add("SupplierName", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("CustomerCode", typeof(string));
            table.Columns.Add("Ean", typeof(string));
            table.Columns.Add("InStock", typeof(double));
            table.Columns.Add("Weight", typeof(double));
            table.Columns.Add("Fits", typeof(string));
            table.Columns.Add("Unit", typeof(string));
            table.Columns.Add("CurrencyPrice", typeof(string));
            table.Columns.Add("Substitutes", typeof(string));
            table.Columns.Add("IntegrationCompany", typeof(int));
            table.Columns.Add("IntegrationId", typeof(int));
            table.Columns.Add("DeliveryType", typeof(int));
            table.Columns.Add("PriceNet", typeof(decimal));
            table.Columns.Add("PriceGross", typeof(decimal));
            table.Columns.Add("Package", typeof(decimal));

            foreach (var product in products)
            {
                table.Rows.Add(
                    product.Code,
                    product.Name ?? string.Empty,
                    DBNull.Value,
                    DBNull.Value,
                    product.Description ?? (object)DBNull.Value,
                    DBNull.Value,
                    product.Ean ?? (object)DBNull.Value,
                    Convert.ToDouble(product.InStock),
                    Convert.ToDouble(product.Weight),
                    product.Fits ?? (object)DBNull.Value,
                    product.Unit ?? (object)DBNull.Value,
                    product.CurrencyPrice ?? (object)DBNull.Value,
                    product.Substitutes ?? (object)DBNull.Value,
                    (int)ServiceConstants.Company,
                    product.IntegrationId,
                    product.DeliveryType,
                    product.PriceNet,
                    product.PriceGross,
                    product.Package);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "RolmarProducts_UpsertBatch",
                    new { Products = table.AsTableValuedParameter("dbo.RolmarProductUpsertType") },
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));

            var codes = products
                .Select(p => p.Code)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var idMap = (await connection.QueryAsync<(int Id, string Code)>(
                "SELECT Id, Code FROM RolmarProducts WHERE IntegrationCompany = @IntegrationCompany AND Code IN @Codes",
                new { IntegrationCompany = ServiceConstants.Company, Codes = codes })).ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);

            using var transaction = connection.BeginTransaction();
            try
            {
                foreach (var product in products)
                {
                    if (!idMap.TryGetValue(product.Code, out var productId))
                        continue;

                    var specsTable = new DataTable();
                    specsTable.Columns.Add("Name", typeof(string));
                    specsTable.Columns.Add("Value", typeof(string));
                    specsTable.Columns.Add("UnitName", typeof(string));

                    foreach (var s in product.Specifications ?? Enumerable.Empty<ProductSpecification>())
                    {
                        specsTable.Rows.Add(s.Name ?? string.Empty, s.Value ?? (object)DBNull.Value, s.UnitName ?? (object)DBNull.Value);
                    }

                    await connection.ExecuteAsync(
                        "ProductSpecifications_ReplaceByProductId",
                        new { ProductId = productId, Items = specsTable.AsTableValuedParameter("dbo.ProductSpecificationType") },
                        transaction,
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 900);

                    var catTable = new DataTable();
                    catTable.Columns.Add("Name", typeof(string));

                    foreach (var c in product.Categories ?? Enumerable.Empty<RolmarCategory>())
                    {
                        catTable.Rows.Add(c.Name ?? string.Empty);
                    }

                    await connection.ExecuteAsync(
                        "RolmarCategory_ReplaceByProductId",
                        new { ProductId = productId, Items = catTable.AsTableValuedParameter("dbo.RolmarCategoryType") },
                        transaction,
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 900);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public Task<bool> DeleteProduct(int productId, CancellationToken ct)
        {
            throw new NotImplementedException();
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

        public Task<List<RolmarProduct>> GetProductsForDetailUpdate(int limit, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public async Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                ProductApplication,
                ProductSpecification,
                RolmarProduct>(
                "RolmarProducts_GetToUpdateParameters",
                (product, application, spec) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Applications = new List<ProductApplication>();
                        existing.Specifications = new List<ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>();

                        productDict.Add(existing.Id, existing);
                    }

                    if (application?.Id > 0 && !existing.Applications.Any(a => a.Id == application.Id))
                        existing.Applications.Add(application);

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    return existing;
                },
                new { IntegrationCompany = ServiceConstants.Company },
                splitOn: "Id,Id",
                commandTimeout: 900,
                commandType: CommandType.StoredProcedure
            );

            return productDict.Values.ToList();
        }

        public async Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, decimal minProductPrice, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            var productDict = new Dictionary<int, RolmarProduct>();

            await connection.QueryAsync<
                RolmarProduct,
                ProductSpecification,
                ProductParameter,
                ProductApplication,
                ProductPackage,
                RolmarProduct>(
                "RolmarProducts_GetToUpload",
                (product, spec, param, application, package) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<ProductSpecification>();
                        existing.Parameters = new List<ProductParameter>();
                        existing.Applications = new List<ProductApplication>();
                        existing.Packages = new List<ProductPackage>();

                        productDict.Add(existing.Id, existing);
                    }

                    if (spec?.Id > 0 && !existing.Specifications.Any(s => s.Id == spec.Id))
                        existing.Specifications.Add(spec);

                    if (param?.Id > 0 && !existing.Parameters.Any(p => p.Id == param.Id))
                        existing.Parameters.Add(param);

                    if (application?.Id > 0 && !existing.Applications.Any(p => p.Id == application.Id))
                        existing.Applications.Add(application);

                    if (package?.Id > 0 && !existing.Packages.Any(p => p.Id == package.Id))
                        existing.Packages.Add(package);

                    return existing;
                },
                new { MinProductStock = minProductStock, MinProductPrice = minProductPrice, IntegrationCompany = ServiceConstants.Company, Account = ServiceConstants.Account },
                splitOn: "Id,Id,Id,Id",
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
                ProductSpecification,
                RolmarProduct>(
                "RolmarProducts_GetWithoutDefaultCategory",
                (product, spec) =>
                {
                    if (!productDict.TryGetValue(product.Id, out var existing))
                    {
                        existing = product;
                        existing.Specifications = new List<ProductSpecification>();
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

        public Task UpdateCompatibilitySet(int productId, bool value, CancellationToken ct)
        {
            throw new NotImplementedException();
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
                commandType: CommandType.StoredProcedure, commandTimeout: 900);
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
                commandType: CommandType.StoredProcedure, commandTimeout: 900
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