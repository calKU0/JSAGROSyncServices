using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Shared.Data;
using System.Data;
using System.Text.RegularExpressions;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly DapperContext _context;
        private readonly ILogger<ProductRepository> _logger;

        public ProductRepository(DapperContext context, ILogger<ProductRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<RolmarProduct>> GetProductsForDetailUpdate(int limit, CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            return (await conn.QueryAsync<RolmarProduct>(
                "RolmarProducts_GetForDetailUpdate",
                new { Limit = limit, IntegrationCompany = ServiceConstants.Company },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 900)).ToList();
        }

        public async Task<bool> DeleteProduct(int productId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> UpsertProductAsync(RolmarProduct product, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var rootBrands = product.Applications?
                    .Where(a => a.ParentID == 0)
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList() ??

                (await connection.QueryAsync<string>(
                    "SELECT pa.Name FROM ProductApplications pa JOIN RolmarProducts rp on rp.Id = pa.ProductId WHERE rp.Code = @ProductCode AND pa.ParentID = 0 AND IntegrationCompany = @IntegrationCompany",
                    new { ProductCode = product.Code, IntegrationCompany = ServiceConstants.Company },
                    transaction))
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();

                var substitues = product.Substitutes ?? await connection.ExecuteScalarAsync<string>(
                  "SELECT Substitutes FROM RolmarProducts WHERE Code = @ProductCode AND NULLIF(Substitutes,'') is not null AND IntegrationCompany = @IntegrationCompany",
                  new { ProductCode = product.Code, IntegrationCompany = ServiceConstants.Company },
                  transaction);

                product.Name = FixName(
                    product.Name,
                    product.Code,
                    product.SupplierName,
                    rootBrands,
                    substitues?.Split(',').ToList()
                );

                var productId = await connection.ExecuteScalarAsync<int>(
                    "RolmarProducts_Upsert",
                    new
                    {
                        Code = product.Code,
                        Name = product.Name,
                        SupplierLogo = product.SupplierLogo,
                        SupplierName = product.SupplierName,
                        Description = product.Description,
                        CustomerCode = product.CustomerCode,
                        Ean = product.Ean,
                        InStock = product.InStock,
                        Weight = product.Weight,
                        Fits = product.Fits,
                        Unit = product.Unit,
                        Currency = product.CurrencyPrice,
                        Substitutes = product.Substitutes,
                        IntegrationCompany = ServiceConstants.Company,
                        IntegrationId = product.IntegrationId,
                        DeliveryType = product.DeliveryType,
                        PriceNet = product.PriceNet,
                        PriceGross = product.PriceGross,
                        Package = product.Package
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure
                );

                if (product.Specifications?.Any() == true)
                {
                    // Replace specifications
                    await connection.ExecuteAsync(
                        "ProductSpecifications_DeleteByProductId",
                        new { ProductId = productId },
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );

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

                if (product.Categories?.Any() == true)
                {
                    // Replace categories
                    await connection.ExecuteAsync(
                        "RolmarCategory_DeleteByProductId",
                        new { ProductId = productId },
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );

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

                if (product.Packages?.Any() == true)
                {
                    // Replace packages
                    await connection.ExecuteAsync(
                        "ProductPackages_DeleteByProductId",
                        new { ProductId = productId },
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );

                    var packages = product.Packages.Select(p => new
                    {
                        ProductId = productId,
                        p.PackUnit,
                        p.PackQty,
                        p.PackNettWeight,
                        p.PackGrossWeight,
                        p.PackEan,
                        p.PackRequired
                    });

                    await connection.ExecuteAsync(
                        "ProductPackages_Insert",
                        packages,
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );
                }

                if (product.Applications?.Any() == true)
                {
                    // Replace applications
                    await connection.ExecuteAsync(
                        "ProductApplications_DeleteByProductId",
                        new { ProductId = productId },
                        transaction,
                        commandType: CommandType.StoredProcedure
                    );

                    var applications = product.Applications.Select(a => new
                    {
                        ProductId = productId,
                        a.ApplicationId,
                        a.ParentID,
                        a.Name
                    });

                    await connection.ExecuteAsync(
                        "ProductApplications_Insert",
                        applications,
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

        private string FixName(string name, string code, string? supplierName, List<string>? rootBrands = null, List<string>? crossNumbers = null)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                bool jagRemoved = false;

                // 1. Remove JAG variants
                name = Regex.Replace(
                    name,
                    @"\bJAG(?=[0-9\-])[\w\-/]*",
                    m =>
                    {
                        jagRemoved = true;
                        return "";
                    },
                    RegexOptions.IgnoreCase
                );

                // Collapse multiple spaces
                name = Regex.Replace(name, @"\s+", " ").Trim();

                string? rest = null;
                string? extractedCode = null;

                var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                // Extract code: first word containing digits anywhere
                var codeMatch = Regex.Match(name, @"\b[0-9A-Za-z]*\d[0-9A-Za-z./-]*\b");
                if (codeMatch.Success)
                {
                    extractedCode = codeMatch.Value;

                    // Remove extracted code from name but preserve word order
                    var restWords = name.Split(' ').Where(w => !string.Equals(w, extractedCode, StringComparison.OrdinalIgnoreCase)).ToList();
                    rest = string.Join(" ", restWords);
                }
                else
                {
                    // fallback if no code detected
                    if (words.Length > 1)
                    {
                        extractedCode = "";
                        rest = string.Join(" ", words);
                    }
                    else
                    {
                        extractedCode = "";
                        rest = name;
                    }
                }

                // 3. Append CodeGaska if JAG removed or short name
                bool codeGaskaAppended = (jagRemoved || rest.Split(' ').Length < 3) && !string.IsNullOrWhiteSpace(code);

                // 4. Insert root brands at the end of descriptor, before code
                var descriptorWords = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                if (rootBrands != null && rootBrands.Count > 0)
                {
                    foreach (var brand in rootBrands.Where(b => !string.IsNullOrWhiteSpace(b)))
                    {
                        if (!descriptorWords.Any(w => string.Equals(w, brand, StringComparison.OrdinalIgnoreCase)))
                            descriptorWords.Add(brand);
                    }
                }

                // 5. Rebuild final name: descriptor + root brands + extracted code + CodeGaska
                var nameParts = new List<string>();
                if (descriptorWords.Any())
                    nameParts.Add(string.Join(" ", descriptorWords));

                var jagRegex = new Regex(@"\bJAG(?=[0-9\-])[\w\-/]*", RegexOptions.IgnoreCase);

                if (jagRemoved && jagRegex.IsMatch(code))
                {
                    // Filter out crossnumbers that match the JAG regex
                    var validCrossNumbers = crossNumbers?
                        .Where(cn => !string.IsNullOrWhiteSpace(cn) && !jagRegex.IsMatch(cn))
                        .Take(2) // only consider up to 2
                        .ToList() ?? new List<string>();

                    if (validCrossNumbers.Any())
                    {
                        // Try adding both if possible, otherwise one, respecting 75-char limit
                        var tempName = string.Join(" ", nameParts);
                        string withTwo = $"{tempName} {string.Join(" ", validCrossNumbers)}".Trim();
                        string withOne = $"{tempName} {validCrossNumbers.First()}".Trim();

                        if (withTwo.Length <= 75)
                            nameParts.AddRange(validCrossNumbers);
                        else if (withOne.Length <= 75)
                            nameParts.Add(validCrossNumbers.First());
                        // else: don't add any
                    }
                }
                else if (codeGaskaAppended)
                {
                    // Default behavior: add code
                    nameParts.Add(code);
                }

                if (!string.IsNullOrWhiteSpace(extractedCode))
                    nameParts.Add(extractedCode);

                name = string.Join(" ", nameParts).Trim();

                var newWords = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToArray();

                // 4. If CodeGaska is in the name AND <3 words → add SupplierName or 'a'
                if (codeGaskaAppended && newWords.Length < 3)
                {
                    var firstCross = crossNumbers?.FirstOrDefault() ?? "";
                    if (!string.IsNullOrWhiteSpace(supplierName))
                    {
                        name = $"{name} {supplierName}".Trim();
                    }
                    else
                    {
                        name = $"{name} {firstCross}".Trim();
                    }
                }

                newWords = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToArray();
                if (newWords.Length < 3)
                {
                    name = $"{name} a".Trim();
                }

                // 5. If longer than 75 chars → remove last words until < 75
                while (name.Length > 75)
                {
                    var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (parts.Count <= 1) break; // stop if only 1 word left
                    parts.RemoveAt(parts.Count - 1);
                    name = string.Join(" ", parts);
                }
            }
            return name;
        }

        public Task UpdateCompatibilitySet(int productId, bool value, CancellationToken ct)
        {
            var sql = @"
                UPDATE Products
                SET BuildCompatibilitySet = @Value
                WHERE Id = @ProductId;";

            using var conn = _context.CreateConnection();
            return conn.ExecuteAsync(sql, new { Value = value, ProductId = productId });
        }

        public Task<bool> UpdateProductStockAsync(string productCode, int stock, CancellationToken ct)
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

        public Task<List<RolmarProduct>> GetNotExistingProductsInAllegro(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task UpdateProductAllegroId(int productId, string allegroProductId, string allegroCategoryId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}