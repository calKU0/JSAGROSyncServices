using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using Dapper;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
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

        public async Task UpsertProductsBatchAsync(List<RolmarProduct> products, CancellationToken ct)
        {
            if (products == null || products.Count == 0)
                return;

            using var connection = _context.CreateConnection();
            connection.Open();

            var codes = products
                .Select(p => p.Code)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingRows = (await connection.QueryAsync<(string Code, string? Substitutes, string? RootBrand)>(
                @"SELECT rp.Code, rp.Substitutes, pa.Name AS RootBrand
                  FROM RolmarProducts rp
                  LEFT JOIN ProductApplications pa ON pa.ProductId = rp.Id AND pa.ParentID = 0
                  WHERE rp.IntegrationCompany = @IntegrationCompany
                    AND rp.Code IN @Codes",
                new { IntegrationCompany = ServiceConstants.Company, Codes = codes })).ToList();

            var substitutesByCode = existingRows
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Substitutes).FirstOrDefault(), StringComparer.OrdinalIgnoreCase);

            var rootBrandsByCode = existingRows
                .Where(x => !string.IsNullOrWhiteSpace(x.RootBrand))
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.RootBrand!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    StringComparer.OrdinalIgnoreCase);

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
                rootBrandsByCode.TryGetValue(product.Code, out var rootBrands);
                substitutesByCode.TryGetValue(product.Code, out var existingSubstitutes);
                var substitutes = product.Substitutes ?? existingSubstitutes;

                product.Name = FixName(
                    product.Name,
                    product.Code,
                    product.SupplierName,
                    rootBrands,
                    substitutes?.Split(',').Distinct().ToList());

                table.Rows.Add(
                    product.Code,
                    product.Name ?? string.Empty,
                    product.SupplierLogo ?? (object)DBNull.Value,
                    product.SupplierName ?? (object)DBNull.Value,
                    product.Description ?? (object)DBNull.Value,
                    product.CustomerCode ?? (object)DBNull.Value,
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
                    await ReplaceSpecificationsAsync(connection, transaction, productId, product.Specifications, ct);
                }

                if (product.Categories?.Any() == true)
                {
                    await ReplaceCategoriesAsync(connection, transaction, productId, product.Categories, ct);
                }

                if (product.Packages?.Any() == true)
                {
                    await ReplacePackagesAsync(connection, transaction, productId, product.Packages, ct);
                }

                if (product.Applications?.Any() == true)
                {
                    await ReplaceApplicationsAsync(connection, transaction, productId, product.Applications, ct);
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

        private async Task ReplaceSpecificationsAsync(IDbConnection connection, IDbTransaction transaction, int productId, List<ProductSpecification> specifications, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Value", typeof(string));
            table.Columns.Add("UnitName", typeof(string));

            foreach (var s in specifications)
            {
                table.Rows.Add(s.Name ?? string.Empty, s.Value ?? (object)DBNull.Value, s.UnitName ?? (object)DBNull.Value);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "ProductSpecifications_ReplaceByProductId",
                    new
                    {
                        ProductId = productId,
                        Items = table.AsTableValuedParameter("dbo.ProductSpecificationType")
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
        }

        private async Task ReplaceCategoriesAsync(IDbConnection connection, IDbTransaction transaction, int productId, List<RolmarCategory> categories, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("Name", typeof(string));

            foreach (var c in categories)
            {
                table.Rows.Add(c.Name ?? string.Empty);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "RolmarCategory_ReplaceByProductId",
                    new
                    {
                        ProductId = productId,
                        Items = table.AsTableValuedParameter("dbo.RolmarCategoryType")
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
        }

        private async Task ReplacePackagesAsync(IDbConnection connection, IDbTransaction transaction, int productId, List<ProductPackage> packages, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("PackUnit", typeof(string));
            table.Columns.Add("PackQty", typeof(double));
            table.Columns.Add("PackNettWeight", typeof(double));
            table.Columns.Add("PackGrossWeight", typeof(double));
            table.Columns.Add("PackEan", typeof(string));
            table.Columns.Add("PackRequired", typeof(int));

            foreach (var p in packages)
            {
                table.Rows.Add(
                    p.PackUnit ?? (object)DBNull.Value,
                    Convert.ToDouble(p.PackQty),
                    Convert.ToDouble(p.PackNettWeight),
                    Convert.ToDouble(p.PackGrossWeight),
                    p.PackEan ?? (object)DBNull.Value,
                    p.PackRequired);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "ProductPackages_ReplaceByProductId",
                    new
                    {
                        ProductId = productId,
                        Items = table.AsTableValuedParameter("dbo.ProductPackageType")
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
        }

        private async Task ReplaceApplicationsAsync(IDbConnection connection, IDbTransaction transaction, int productId, List<ProductApplication> applications, CancellationToken ct)
        {
            var table = new DataTable();
            table.Columns.Add("ApplicationId", typeof(int));
            table.Columns.Add("ParentID", typeof(int));
            table.Columns.Add("Name", typeof(string));

            foreach (var a in applications)
            {
                table.Rows.Add(a.ApplicationId, a.ParentID, a.Name ?? (object)DBNull.Value);
            }

            await connection.ExecuteAsync(
                new CommandDefinition(
                    "ProductApplications_ReplaceByProductId",
                    new
                    {
                        ProductId = productId,
                        Items = table.AsTableValuedParameter("dbo.ProductApplicationType")
                    },
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900,
                    cancellationToken: ct));
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
                commandType: CommandType.StoredProcedure,
                commandTimeout: 900);
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
                commandType: CommandType.StoredProcedure, commandTimeout: 900);
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

                if (newWords.Length < 13)
                {
                    var suffix = (crossNumbers?.LastOrDefault() ?? code)?.Trim();
                    var hasSuffixAlready = !string.IsNullOrWhiteSpace(suffix) &&
                        name.Contains(suffix, StringComparison.OrdinalIgnoreCase);
                    var hasJagAlready = name.Contains(" JAG", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("JAG", StringComparison.OrdinalIgnoreCase);

                    if (!hasSuffixAlready && !hasJagAlready && !string.IsNullOrWhiteSpace(suffix))
                    {
                        name = $"{name} {suffix} JAG".Trim();
                    }
                }

                // Insert space inside words longer than or equal to 30 characters
                name = Regex.Replace(name, @"\S{30,}", m =>
                {
                    var word = m.Value;

                    // Try to find a natural split near position 30
                    int splitPos = -1;

                    // look for separator between 20 and 35 chars
                    for (int i = Math.Min(35, word.Length - 1); i >= Math.Max(20, 1); i--)
                    {
                        if (word[i] == '-' || word[i] == '/' || word[i] == '_' || word[i] == '.')
                        {
                            splitPos = i + 1;
                            break;
                        }
                    }

                    // fallback: hard split at 30
                    if (splitPos == -1)
                        splitPos = 30;

                    return word.Substring(0, splitPos) + " " + word.Substring(splitPos);
                });

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

        public async Task UpdateCompatibilitySet(int productId, bool value, CancellationToken ct)
        {
            var sql = @"
                UPDATE RolmarProducts
                SET BuildCompatibilitySet = @Value
                WHERE Id = @ProductId;";

            using var conn = _context.CreateConnection();
            conn.Open();
            await conn.ExecuteAsync(sql, new { Value = value, ProductId = productId });
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