using Dapper;
using AllegroGaskaProductsSyncService.Data;
using AllegroGaskaProductsSyncService.DTOs;
using AllegroGaskaProductsSyncService.Models;
using AllegroGaskaProductsSyncService.Models.Product;
using AllegroGaskaProductsSyncService.Repositories.Interfaces;
using Serilog;
using System.Text.RegularExpressions;

namespace AllegroGaskaProductsSyncService.Repositories
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

        public async Task<Product> GetByIdAsync(int id, CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            const string sql = @"SELECT * FROM Products WHERE Id = @Id;
                                 SELECT * FROM ProductAttributes WHERE ProductId = @Id;";

            using var multi = await conn.QueryMultipleAsync(sql, new { Id = id });
            var product = await multi.ReadFirstOrDefaultAsync<Product>();
            if (product != null)
                product.Atributes = (await multi.ReadAsync<ProductAttribute>()).ToList();

            return product;
        }

        public async Task<List<Product>> GetProductsForDetailUpdate(int limit, CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            var sql = @"
                SELECT *
                FROM Products p
                WHERE NOT EXISTS (SELECT 1 FROM ProductCategories pc WHERE pc.ProductId = p.Id)
                  AND p.Archived = 0
                ORDER BY p.Id
                OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY;";

            return (await conn.QueryAsync<Product>(sql, new { Limit = limit })).ToList();
        }

        public async Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            if (fetchedProductIds == null || fetchedProductIds.Count == 0)
            {
                _logger.LogWarning("No fetched products provided, skipping archiving.");
                return 0;
            }

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            // 1. Clear staging table
            await conn.ExecuteAsync("TRUNCATE TABLE ProductSyncTemp;", transaction: tran);

            // 2. Insert fetched IDs in batches
            const int batchSize = 1000;
            var ids = fetchedProductIds.ToList();
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).Select(x => $"({x})");
                var sqlInsert = $"INSERT INTO ProductSyncTemp (ProductId) VALUES {string.Join(",", batch)};";
                await conn.ExecuteAsync(sqlInsert, transaction: tran);
            }

            // 3. Archive missing products
            var sqlArchive = @"
                UPDATE Products
                SET Archived = 1
                WHERE Archived = 0
                  AND Id NOT IN (SELECT ProductId FROM ProductSyncTemp);";

            var archivedCount = await conn.ExecuteAsync(sqlArchive, transaction: tran);
            tran.Commit();

            return archivedCount;
        }

        public async Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            if (apiProducts == null || apiProducts.Count == 0)
                return;

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            // 1. Track fetched IDs
            foreach (var p in apiProducts)
                fetchedProductIds.Add(p.Id);

            // 2. Load existing products (with applications) in one query
            var productIds = apiProducts.Select(p => p.Id).ToList();

            var sql = @"
                SELECT p.*, a.*
                FROM Products p
                LEFT JOIN Applications a ON a.ProductId = p.Id
                WHERE p.Id IN @Ids";

            var productDictionary = new Dictionary<int, Product>();

            var existingProductsQuery = await conn.QueryAsync<Product, Application, Product>(
                sql,
                (product, application) =>
                {
                    if (!productDictionary.TryGetValue(product.Id, out var productEntry))
                    {
                        productEntry = product;
                        productEntry.Applications = new List<Application>();
                        productDictionary.Add(productEntry.Id, productEntry);
                    }

                    if (application != null)
                        productEntry.Applications.Add(application);

                    return productEntry;
                },
                new { Ids = productIds },
                splitOn: "Id",  // Tells Dapper where Application columns start
                transaction: tran
            );

            var existingProducts = productDictionary.ToDictionary(p => p.Key, p => p.Value);

            var toInsert = new List<Product>();
            var toUpdate = new List<Product>();

            foreach (var apiProduct in apiProducts)
            {
                existingProducts.TryGetValue(apiProduct.Id, out var product);

                // Prepare root brands from existing applications
                var rootBrands = product?.Applications?
                    .Where(a => a.ParentID == 0)
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList() ?? new List<string>();

                if (product == null)
                {
                    product = new Product { Id = apiProduct.Id, CreatedDate = DateTime.UtcNow, UpdatedDate = DateTime.UtcNow };
                    toInsert.Add(product);
                }
                else
                {
                    toUpdate.Add(product);
                }

                // Map fields
                product.Name = FixName(apiProduct.Name, apiProduct.CodeGaska, apiProduct.CodeCustomer, rootBrands);
                product.CodeGaska = apiProduct.CodeGaska;
                product.CodeCustomer = apiProduct.CodeCustomer;
                product.Description = apiProduct.Description;
                product.Ean = apiProduct.Ean;
                product.TechnicalDetails = apiProduct.TechnicalDetails;
                product.WeightGross = apiProduct.GrossWeight;
                product.WeightNet = apiProduct.NetWeight;
                product.SupplierName = apiProduct.SupplierName;
                product.SupplierLogo = apiProduct.SupplierLogo;
                product.InStock = apiProduct.InStock;
                product.Unit = apiProduct.Unit;
                product.CurrencyPrice = apiProduct.CurrencyPrice;
                product.PriceNet = apiProduct.NetPrice;
                product.PriceGross = apiProduct.GrossPrice;
                product.DeliveryType = apiProduct.DeliveryType;
                product.Archived = false;
            }

            // 3. Bulk insert and update
            if (toInsert.Any())
            {
                const string insertSql = @"
                    INSERT INTO Products
                    (Id, Name, CodeGaska, CodeCustomer, Description, Ean, TechnicalDetails,
                     WeightGross, WeightNet, SupplierName, SupplierLogo, InStock, Unit,
                     CurrencyPrice, PriceNet, PriceGross, DeliveryType, Archived, CreatedDate, UpdatedDate)
                    VALUES
                    (@Id, @Name, @CodeGaska, @CodeCustomer, @Description, @Ean, @TechnicalDetails,
                     @WeightGross, @WeightNet, @SupplierName, @SupplierLogo, @InStock, @Unit,
                     @CurrencyPrice, @PriceNet, @PriceGross, @DeliveryType, @Archived, @CreatedDate, @UpdatedDate);";

                await conn.ExecuteAsync(insertSql, toInsert, tran);
            }

            if (toUpdate.Any())
            {
                const string updateSql = @"
                    UPDATE Products
                    SET Name = @Name,
                        CodeGaska = @CodeGaska,
                        CodeCustomer = @CodeCustomer,
                        Description = @Description,
                        Ean = @Ean,
                        TechnicalDetails = @TechnicalDetails,
                        WeightGross = @WeightGross,
                        WeightNet = @WeightNet,
                        SupplierName = @SupplierName,
                        SupplierLogo = @SupplierLogo,
                        InStock = @InStock,
                        Unit = @Unit,
                        CurrencyPrice = @CurrencyPrice,
                        PriceNet = @PriceNet,
                        PriceGross = @PriceGross,
                        DeliveryType = @DeliveryType,
                        Archived = @Archived
                    WHERE Id = @Id;";

                await conn.ExecuteAsync(updateSql, toUpdate, tran);
            }

            tran.Commit();
        }

        public async Task UpdateProductDetails(int productId, ApiProduct updatedProduct, CancellationToken ct)
        {
            if (updatedProduct == null)
                throw new ArgumentNullException(nameof(updatedProduct));

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            try
            {
                // Generic method to sync child entities
                async Task SyncEntities<TDb, TApi>(
                    IEnumerable<TApi> apiItems,
                    string tableName,
                    Func<TApi, TDb> map,
                    Func<TDb, object> keySelector,
                    Func<TDb, object> idSelector)
                {
                    apiItems ??= Enumerable.Empty<TApi>();

                    var dbItems = (await conn.QueryAsync<TDb>(
                        $"SELECT * FROM {tableName} WHERE ProductId = @ProductId",
                        new { ProductId = productId }, tran)).ToList();

                    var apiList = apiItems.Select(map).ToList();

                    // Delete items not in API
                    var toDelete = dbItems
                        .Where(db => !apiList.Any(api => keySelector(api).Equals(keySelector(db))))
                        .ToList();

                    foreach (var del in toDelete)
                        await conn.ExecuteAsync($"DELETE FROM {tableName} WHERE Id = @Id", new { Id = idSelector(del) }, tran);

                    // Update existing items
                    foreach (var dbItem in dbItems)
                    {
                        var apiItem = apiList.FirstOrDefault(a => keySelector(a).Equals(keySelector(dbItem)));
                        if (apiItem == null) continue;

                        var props = typeof(TDb).GetProperties()
                            .Where(p => p.Name != "Id" && p.Name != "ProductId" &&
                                        (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
                            .ToArray();

                        var setSql = string.Join(",", props.Select(p => $"{p.Name} = @{p.Name}"));
                        var sql = $"UPDATE {tableName} SET {setSql} WHERE Id = @Id";
                        await conn.ExecuteAsync(sql, apiItem, tran);
                    }

                    // Insert new items
                    var toInsert = apiList
                        .Where(api => !dbItems.Any(db => keySelector(api).Equals(keySelector(db))))
                        .ToList();

                    foreach (var item in toInsert)
                    {
                        var insertProps = typeof(TDb).GetProperties()
                        .Where(p => p.Name != "Id" &&
                                    (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
                        .ToArray();
                        var colNames = string.Join(",", insertProps.Select(p => p.Name));
                        var paramNames = string.Join(",", insertProps.Select(p => "@" + p.Name));
                        var sql = $"INSERT INTO {tableName} ({colNames}) VALUES ({paramNames})";
                        await conn.ExecuteAsync(sql, item, tran);
                    }
                }

                // Sync all child tables
                await SyncEntities(
                    updatedProduct.Packages,
                    "Packages",
                    p => new Package
                    {
                        ProductId = productId,
                        PackUnit = p.PackUnit,
                        PackQty = p.PackQty,
                        PackNettWeight = p.PackNettWeight,
                        PackGrossWeight = p.PackGrossWeight,
                        PackEan = p.PackEan,
                        PackRequired = p.PackRequired
                    },
                    p => p.PackEan, // unique key
                    p => p.Id
                );

                await SyncEntities(
                    updatedProduct.CrossNumbers?.SelectMany(c => (c.CrossNumber ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(cn => new CrossNumber
                        {
                            ProductId = productId,
                            CrossNumberValue = cn.Trim(),
                            CrossManufacturer = c.CrossManufacturer
                        })),
                    "CrossNumbers",
                    x => x,
                    x => (x.CrossNumberValue, x.CrossManufacturer),
                    x => x.Id
                );

                await SyncEntities(
                    updatedProduct.Components,
                    "Components",
                    c => new Component
                    {
                        ProductId = productId,
                        TwrID = c.TwrID,
                        CodeGaska = c.CodeGaska,
                        Qty = c.Qty
                    },
                    c => c.TwrID,
                    c => c.Id
                );

                await SyncEntities(
                    updatedProduct.RecommendedParts,
                    "RecommendedParts",
                    r => new RecommendedPart
                    {
                        ProductId = productId,
                        TwrID = r.TwrID,
                        CodeGaska = r.CodeGaska,
                        Qty = r.Qty
                    },
                    r => r.TwrID,
                    r => r.Id
                );

                await SyncEntities(
                    updatedProduct.Applications,
                    "Applications",
                    a => new Application
                    {
                        ProductId = productId,
                        ApplicationId = a.Id,
                        ParentID = a.ParentID,
                        Name = a.Name
                    },
                    a => a.ApplicationId,
                    a => a.Id
                );

                await SyncEntities(
                    updatedProduct.Parameters,
                    "ProductAttributes",
                    p => new ProductAttribute
                    {
                        ProductId = productId,
                        AttributeId = p.AttributeId,
                        AttributeName = p.AttributeName,
                        AttributeValue = p.AttributeValue
                    },
                    p => p.AttributeId,
                    p => p.Id
                );

                await SyncEntities(
                    updatedProduct.Images,
                    "ProductImages",
                    i => new ProductImage
                    {
                        ProductId = productId,
                        Title = i.Title,
                        Url = i.Url,
                        AllegroExpirationDate = DateTime.UtcNow
                    },
                    i => i.Url,
                    i => i.Id
                );

                await SyncEntities(
                    updatedProduct.Files,
                    "ProductFiles",
                    f => new ProductFile
                    {
                        ProductId = productId,
                        Title = f.Title,
                        Url = f.Url
                    },
                    f => f.Url,
                    f => f.Id
                );

                await SyncEntities(
                    updatedProduct.Categories,
                    "ProductCategories",
                    c => new ProductCategory
                    {
                        ProductId = productId,
                        CategoryId = c.Id,
                        ParentID = c.ParentID,
                        Name = c.Name
                    },
                    c => c.CategoryId,
                    c => c.Id
                );

                // Update main product
                var rootBrands = (updatedProduct.Applications ?? Enumerable.Empty<ApiApplication>())
                    .Where(a => a.ParentID == 0)
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                var updateSql = @"
                    UPDATE Products
                    SET CodeGaska = @CodeGaska,
                        CodeCustomer = @CodeCustomer,
                        SupplierName = @SupplierName,
                        SupplierLogo = @SupplierLogo,
                        InStock = @InStock,
                        CurrencyPrice = @CurrencyPrice,
                        PriceNet = @PriceNet,
                        PriceGross = @PriceGross,
                        DeliveryType = @DeliveryType,
                        UpdatedDate = @UpdatedDate,
                        Name = @Name
                    WHERE Id = @Id;";

                await conn.ExecuteAsync(updateSql, new
                {
                    Id = productId,
                    updatedProduct.CodeGaska,
                    updatedProduct.CodeCustomer,
                    updatedProduct.SupplierName,
                    updatedProduct.SupplierLogo,
                    updatedProduct.InStock,
                    updatedProduct.CurrencyPrice,
                    updatedProduct.PriceNet,
                    updatedProduct.PriceGross,
                    updatedProduct.DeliveryType,
                    UpdatedDate = DateTime.Now,
                    Name = FixName(updatedProduct.Name, updatedProduct.CodeGaska, updatedProduct.CodeCustomer, rootBrands)
                }, tran);

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task<List<Product>> GetProductsWithoutDefaultCategory(CancellationToken ct)
        {
            const string sql = @"
        SELECT p.*, a.*
        FROM Products p
        LEFT JOIN Applications a ON a.ProductId = p.Id
        WHERE p.DefaultAllegroCategory = 0
          AND p.Archived = 0
          AND EXISTS (
              SELECT 1
              FROM ProductCategories pc
              WHERE pc.ProductId = p.Id
          );";

            using var conn = _context.CreateConnection();

            var productDictionary = new Dictionary<int, Product>();

            var products = await conn.QueryAsync<Product, Application, Product>(
                sql,
                (product, application) =>
                {
                    if (!productDictionary.TryGetValue(product.Id, out var productEntry))
                    {
                        productEntry = product;
                        productEntry.Applications = new List<Application>();
                        productDictionary.Add(productEntry.Id, productEntry);
                    }

                    if (application != null)
                        productEntry.Applications.Add(application);

                    return productEntry;
                },
                splitOn: "Id",
                commandTimeout: 60,
                transaction: null
            );

            return productDictionary.Values.ToList();
        }

        public async Task<List<Product>> GetProductsToUpdateParameters(CancellationToken ct)
        {
            const string sql = @"
                SELECT p.*
                FROM Products p
                WHERE EXISTS (SELECT 1 FROM ProductCategories pc WHERE pc.ProductId = p.Id)
                  AND p.Archived = 0
                  AND NOT EXISTS (SELECT 1 FROM ProductParameters pp WHERE pp.ProductId = p.Id)
                  AND p.DefaultAllegroCategory <> 0";

            using var conn = _context.CreateConnection();

            var products = (await conn.QueryAsync<Product>(sql)).ToList();
            if (!products.Any())
                return products;

            var productIds = products.Select(p => p.Id).ToArray();

            var applications = new List<Application>();
            var crossNumbers = new List<CrossNumber>();

            // Split productIds into batches of 1000 (safe for SQL Server)
            foreach (var batch in productIds.Chunk(1000))
            {
                var apps = await conn.QueryAsync<Application>(
                    "SELECT * FROM Applications WHERE ProductId IN @batch",
                    new { batch });
                applications.AddRange(apps);

                var crosses = await conn.QueryAsync<CrossNumber>(
                    "SELECT * FROM CrossNumbers WHERE ProductId IN @batch",
                    new { batch });
                crossNumbers.AddRange(crosses);
            }

            // Map to products
            foreach (var p in products)
            {
                p.Applications = applications.Where(a => a.ProductId == p.Id).ToList();
                p.CrossNumbers = crossNumbers.Where(c => c.ProductId == p.Id).ToList();
            }

            return products;
        }

        public async Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct)
        {
            if (categoryId == 0) return;

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            try
            {
                // Delete old parameters not matching the new category
                await conn.ExecuteAsync(@"
                    DELETE pp
                    FROM ProductParameters pp
                    INNER JOIN Products p ON pp.ProductId = p.Id
                    WHERE pp.ProductId = @ProductId AND p.DefaultAllegroCategory <> @CategoryId;",
                    new { ProductId = productId, CategoryId = categoryId },
                    tran
                );

                // Update the product category
                await conn.ExecuteAsync(@"
                    UPDATE Products
                    SET DefaultAllegroCategory = @CategoryId
                    WHERE Id = @ProductId;",
                    new { ProductId = productId, CategoryId = categoryId },
                    tran
                );

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task UpdateProductAllegroCategory(string productCode, string categoryIdStr, CancellationToken ct)
        {
            if (!int.TryParse(categoryIdStr, out var categoryId) || categoryId == 0) return;

            using var conn = _context.CreateConnection();
            conn.Open();
            using var tran = conn.BeginTransaction();

            try
            {
                var product = await conn.QueryFirstOrDefaultAsync<Product>(
                    "SELECT * FROM Products WHERE CodeGaska = @CodeGaska;",
                    new { CodeGaska = productCode },
                    tran
                );

                if (product == null || product.DefaultAllegroCategory == categoryId) return;

                // Delete all parameters for the product
                await conn.ExecuteAsync(
                    "DELETE FROM ProductParameters WHERE ProductId = @ProductId;",
                    new { ProductId = product.Id },
                    tran
                );

                // Update category
                await conn.ExecuteAsync(
                    "UPDATE Products SET DefaultAllegroCategory = @CategoryId WHERE Id = @ProductId;",
                    new { ProductId = product.Id, CategoryId = categoryId },
                    tran
                );

                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }

        public async Task<List<Product>> GetProductsToUpload(CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            var cutoff = DateTime.Now.AddMinutes(60);
            const int pageSize = 500;
            var result = new List<Product>();
            int offset = 0;

            while (true)
            {
                // 1️ Fetch batch of products
                var products = (await conn.QueryAsync<Product>(@"
                    SELECT DISTINCT p.*
                    FROM Products p
                    INNER JOIN ProductCategories pc ON pc.ProductId = p.Id
                    INNER JOIN ProductImages pi ON pi.ProductId = p.Id
                    WHERE p.Archived = 0
                      AND p.DefaultAllegroCategory <> 0
                      AND p.PriceGross > 1
                      AND p.InStock > 0
                      AND NOT EXISTS (SELECT 1 FROM AllegroOffers ao WHERE ao.ExternalId = p.CodeGaska)
                      AND pi.AllegroUrl IS NOT NULL
                      AND pi.AllegroExpirationDate >= @Cutoff
                    ORDER BY p.Id
                    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
                    new { Cutoff = cutoff, Offset = offset, PageSize = pageSize }
                )).ToList();

                if (!products.Any())
                    break;

                var productIds = products.Select(p => p.Id).ToArray();

                // 2️ Load child entities
                var images = (await conn.QueryAsync<ProductImage>(@"
                    SELECT ProductId, AllegroUrl, MAX(AllegroLogoUrl) AS AllegroLogoUrl
                    FROM ProductImages
                    WHERE ProductId IN @ProductIds
                      AND AllegroUrl IS NOT NULL
                      AND AllegroExpirationDate >= @Cutoff
                    GROUP BY ProductId, AllegroUrl;",
                    new { ProductIds = productIds, Cutoff = cutoff }
                )).ToList();

                var packages = (await conn.QueryAsync<Package>(@"
                    SELECT *
                    FROM Packages
                    WHERE ProductId IN @ProductIds;",
                    new { ProductIds = productIds }
                )).ToList();

                var attributes = (await conn.QueryAsync<ProductAttribute>(@"
                    SELECT *
                    FROM ProductAttributes
                    WHERE ProductId IN @ProductIds;",
                    new { ProductIds = productIds }
                )).ToList();

                var crossNumbers = (await conn.QueryAsync<CrossNumber>(@"
                    SELECT *
                    FROM CrossNumbers
                    WHERE ProductId IN @ProductIds;",
                    new { ProductIds = productIds }
                )).ToList();

                var applications = (await conn.QueryAsync<Application>(@"
                    SELECT *
                    FROM Applications
                    WHERE ProductId IN @ProductIds;",
                    new { ProductIds = productIds }
                )).ToList();

                // Load product parameters with category parameters
                var productParameters = (await conn.QueryAsync<ProductParameter, CategoryParameter, ProductParameter>(@"
                    SELECT
                        pp.Id AS PpId, pp.ProductId, pp.CategoryParameterId, pp.Value, pp.IsForProduct,
                        cp.Id AS CpId, cp.ParameterId, cp.CategoryId, cp.Name, cp.Type, cp.Required,
                        cp.RequiredForProduct, cp.DescribesProduct, cp.CustomValuesEnabled, cp.AmbiguousValueId, cp.Min, cp.Max
                    FROM ProductParameters pp
                    INNER JOIN CategoryParameters cp ON cp.Id = pp.CategoryParameterId
                    WHERE pp.ProductId IN @ProductIds;",
                    (pp, cp) =>
                    {
                        pp.CategoryParameter = cp;
                        return pp;
                    },
                    new { ProductIds = productIds },
                    splitOn: "CpId"
                )).ToList();

                // 3️ Map child collections to each product
                foreach (var p in products)
                {
                    p.Images = images
                        .Where(i => i.ProductId == p.Id)
                        .GroupBy(i => i.AllegroUrl)
                        .Select(g => g.First())
                        .ToList();

                    p.Packages = packages
                        .Where(pkg => pkg.ProductId == p.Id)
                        .ToList();

                    p.Atributes = attributes
                        .Where(attr => attr.ProductId == p.Id)
                        .ToList();

                    p.CrossNumbers = crossNumbers
                        .Where(cn => cn.ProductId == p.Id)
                        .ToList();

                    p.Applications = applications
                        .Where(app => app.ProductId == p.Id)
                        .ToList();

                    p.Parameters = productParameters
                        .Where(param => param.ProductId == p.Id)
                        .ToList();
                }

                // Only add products with images
                result.AddRange(products.Where(p => p.Images.Any()));
                offset += pageSize;
            }

            return result;
        }

        public async Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct)
        {
            if (parameters == null || !parameters.Any())
                return;

            const int batchSize = 500;
            var batches = parameters
                .Select((p, i) => new { p, i })
                .GroupBy(x => x.i / batchSize)
                .Select(g => g.Select(x => x.p).ToList());

            using var conn = _context.CreateConnection();
            conn.Open();

            foreach (var batch in batches)
            {
                var productIds = batch.Select(p => p.ProductId).Distinct().ToArray();
                var categoryParamIds = batch.Select(p => p.CategoryParameterId).Distinct().ToArray();

                // Fetch existing parameters
                var existingParams = (await conn.QueryAsync<ProductParameter>(
                    @"SELECT * FROM ProductParameters
                      WHERE ProductId IN @ProductIds AND CategoryParameterId IN @CategoryParamIds;",
                    new { ProductIds = productIds, CategoryParamIds = categoryParamIds }
                )).ToList();

                var existingDict = existingParams.ToDictionary(p => (p.ProductId, p.CategoryParameterId));

                var toInsert = new List<ProductParameter>();

                foreach (var param in batch)
                {
                    if (existingDict.TryGetValue((param.ProductId, param.CategoryParameterId), out var existing))
                    {
                        await conn.ExecuteAsync(
                            @"UPDATE ProductParameters
                              SET Value = @Value, IsForProduct = @IsForProduct
                              WHERE ProductId = @ProductId AND CategoryParameterId = @CategoryParameterId;",
                            new
                            {
                                param.Value,
                                param.IsForProduct,
                                param.ProductId,
                                param.CategoryParameterId
                            }
                        );
                    }
                    else
                    {
                        toInsert.Add(param);
                    }
                }

                if (toInsert.Any())
                {
                    var insertSql = @"
                        INSERT INTO ProductParameters (ProductId, CategoryParameterId, Value, IsForProduct)
                        VALUES (@ProductId, @CategoryParameterId, @Value, @IsForProduct);";
                    await conn.ExecuteAsync(insertSql, toInsert);
                }
            }
        }

        public async Task UpdateParameter(int productId, int parameterId, string value, CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            // Fetch categoryParameterId
            var categoryParameterId = await conn.QueryFirstOrDefaultAsync<int>(
                @"SELECT cp.Id
                  FROM CategoryParameters cp
                  INNER JOIN Products p ON p.DefaultAllegroCategory = cp.CategoryId
                  WHERE p.Id = @ProductId AND cp.ParameterId = @ParameterId;",
                new { ProductId = productId, ParameterId = parameterId }
            );

            if (categoryParameterId == 0) return;

            // Update value
            await conn.ExecuteAsync(
                @"UPDATE ProductParameters
                  SET Value = @Value
                  WHERE ProductId = @ProductId AND CategoryParameterId = @CategoryParameterId;",
                new { Value = value, ProductId = productId, CategoryParameterId = categoryParameterId }
            );
        }

        private string FixName(string name, string code, string supplierName, List<string> rootBrands = null)
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

                string rest = null;
                string extractedCode = null;

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
                if (!string.IsNullOrWhiteSpace(extractedCode))
                    nameParts.Add(extractedCode);
                if (codeGaskaAppended)
                    nameParts.Add(code);

                name = string.Join(" ", nameParts).Trim();

                var newWords = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToArray();

                // 4. If CodeGaska is in the name AND <3 words → add SupplierName or 'a'
                if (codeGaskaAppended && newWords.Length < 3)
                {
                    if (!string.IsNullOrWhiteSpace(supplierName))
                    {
                        name = $"{name} {supplierName}".Trim();
                    }
                    else
                    {
                        name = $"{name} a".Trim();
                    }
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
    }
}