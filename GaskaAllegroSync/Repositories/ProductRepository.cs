using GaskaAllegroSync.Data;
using GaskaAllegroSync.DTOs;
using GaskaAllegroSync.DTOs.AllegroApi;
using GaskaAllegroSync.Models;
using GaskaAllegroSync.Models.Product;
using GaskaAllegroSync.Repositories.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly MyDbContext _context;

        public ProductRepository(MyDbContext context)
        {
            _context = context;
        }

        public async Task<Product> GetByIdAsync(int id, CancellationToken ct)
        {
            return await _context.Products
                .Include(p => p.Atributes)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
        }

        public async Task<List<Product>> GetProductsForDetailUpdate(int limit, CancellationToken ct)
        {
            var productsToUpdate = await _context.Products
                .AsNoTracking()
                .Where(p => !p.Categories.Any() && !p.Archived)
                .Take(limit)
                .ToListAsync(ct);

            return productsToUpdate;
        }

        public async Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            if (fetchedProductIds == null || fetchedProductIds.Count == 0)
            {
                Log.Warning("No fetched products provided, skipping archiving to avoid nuking DB.");
                return 0;
            }

            // Clear staging table
            await _context.Database.ExecuteSqlCommandAsync("TRUNCATE TABLE ProductSyncTemp;", ct);

            // Insert fetched IDs in batches
            const int batchSize = 1000;
            var ids = fetchedProductIds.ToList();

            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var values = string.Join(",", batch.Select(id => $"({id})"));
                var sqlInsert = $"INSERT INTO ProductSyncTemp (ProductId) VALUES {values};";
                await _context.Database.ExecuteSqlCommandAsync(sqlInsert, ct);
            }

            // Archive missing products
            var sqlArchive = @"
                UPDATE Products
                SET Archived = 1
                WHERE Archived = 0
                  AND Id NOT IN (SELECT ProductId FROM ProductSyncTemp);";

            var archivedCount = await _context.Database.ExecuteSqlCommandAsync(sqlArchive, ct);

            return archivedCount;
        }

        public async Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            if (apiProducts == null || !apiProducts.Any())
                return;

            // 1 Collect all IDs
            var productIds = apiProducts.Select(p => p.Id).ToList();
            foreach (var id in productIds)
                fetchedProductIds.Add(id);

            // 2 Load existing products
            var existingProducts = _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToList();

            var existingDict = existingProducts.ToDictionary(p => p.Id);

            var toInsert = new List<Product>();
            var toUpdate = new List<Product>();

            foreach (var apiProduct in apiProducts)
            {
                existingDict.TryGetValue(apiProduct.Id, out var product);

                // Prepare root brands from existing applications
                List<string> rootBrands;
                if (product?.Applications != null)
                {
                    rootBrands = product.Applications
                        .Where(a => a.ParentID == 0)
                        .Select(a => a.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .ToList();
                }
                else
                {
                    rootBrands = new List<string>();
                }

                if (product == null)
                {
                    product = new Product { Id = apiProduct.Id };
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

            // 3 Bulk operations
            if (toInsert.Any())
                _context.BulkInsert(toInsert);

            if (toUpdate.Any())
                _context.BulkUpdate(toUpdate);
        }

        public async Task UpdateProductDetails(int productId, ApiProduct updatedProduct, CancellationToken ct)
        {
            if (updatedProduct == null)
                throw new ArgumentNullException(nameof(updatedProduct));

            // 1️⃣ Load the product
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.Id == productId, ct);

            if (product == null)
                return;

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    // 2️⃣ Delete all child entities in one go
                    var tables = new[]
                    {
                        "Packages", "CrossNumbers", "Components", "RecommendedParts",
                        "Applications", "ProductAttributes", "ProductImages",
                        "ProductFiles", "ProductCategories"
                    };

                    foreach (var table in tables)
                    {
                        await _context.Database.ExecuteSqlCommandAsync(
                            string.Format("DELETE FROM {0} WHERE ProductId = @p0", table),
                            new object[] { productId },
                            ct
                        );
                    }

                    // 3️⃣ Map and bulk insert (null-safe)
                    var packages = (updatedProduct.Packages ?? new List<ApiPackage>())
                        .Select(p => new Package
                        {
                            ProductId = productId,
                            PackUnit = p.PackUnit,
                            PackQty = p.PackQty,
                            PackNettWeight = p.PackNettWeight,
                            PackGrossWeight = p.PackGrossWeight,
                            PackEan = p.PackEan,
                            PackRequired = p.PackRequired
                        })
                        .ToList();

                    var crossNumbers = (updatedProduct.CrossNumbers ?? new List<ApiCrossNumber>())
                        .SelectMany(c =>
                            (c.CrossNumber ?? string.Empty)
                                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(cn => new CrossNumber
                                {
                                    ProductId = productId,
                                    CrossNumberValue = cn.Trim(),
                                    CrossManufacturer = c.CrossManufacturer
                                }))
                        .ToList();

                    var components = (updatedProduct.Components ?? new List<ApiComponent>())
                        .Select(c => new Component
                        {
                            ProductId = productId,
                            TwrID = c.TwrID,
                            CodeGaska = c.CodeGaska,
                            Qty = c.Qty
                        })
                        .ToList();

                    var recommendedParts = (updatedProduct.RecommendedParts ?? new List<ApiRecommendedPart>())
                        .Select(r => new RecommendedPart
                        {
                            ProductId = productId,
                            TwrID = r.TwrID,
                            CodeGaska = r.CodeGaska,
                            Qty = r.Qty
                        })
                        .ToList();

                    var applications = (updatedProduct.Applications ?? new List<ApiApplication>())
                        .Select(a => new Application
                        {
                            ProductId = productId,
                            ApplicationId = a.Id,
                            ParentID = a.ParentID,
                            Name = a.Name
                        })
                        .ToList();

                    var attributes = (updatedProduct.Parameters ?? new List<ApiParameter>())
                        .Select(p => new ProductAttribute
                        {
                            ProductId = productId,
                            AttributeId = p.AttributeId,
                            AttributeName = p.AttributeName,
                            AttributeValue = p.AttributeValue
                        })
                        .ToList();

                    var images = (updatedProduct.Images ?? new List<ApiImage>())
                        .Select(i => new ProductImage
                        {
                            ProductId = productId,
                            Title = i.Title,
                            Url = i.Url
                        })
                        .ToList();

                    var files = (updatedProduct.Files ?? new List<ApiFile>())
                        .Select(f => new ProductFile
                        {
                            ProductId = productId,
                            Title = f.Title,
                            Url = f.Url
                        })
                        .ToList();

                    var categories = (updatedProduct.Categories ?? new List<ApiCategory>())
                        .Select(c => new ProductCategory
                        {
                            ProductId = productId,
                            CategoryId = c.Id,
                            ParentID = c.ParentID,
                            Name = c.Name
                        })
                        .ToList();

                    // 4️⃣ Bulk insert
                    if (packages.Any()) await _context.BulkInsertAsync(packages, ct);
                    if (crossNumbers.Any()) await _context.BulkInsertAsync(crossNumbers, ct);
                    if (components.Any()) await _context.BulkInsertAsync(components, ct);
                    if (recommendedParts.Any()) await _context.BulkInsertAsync(recommendedParts, ct);
                    if (applications.Any()) await _context.BulkInsertAsync(applications, ct);
                    if (attributes.Any()) await _context.BulkInsertAsync(attributes, ct);
                    if (images.Any()) await _context.BulkInsertAsync(images, ct);
                    if (files.Any()) await _context.BulkInsertAsync(files, ct);
                    if (categories.Any()) await _context.BulkInsertAsync(categories, ct);

                    // 5️⃣ Update main product
                    product.CodeGaska = updatedProduct.CodeGaska;
                    product.CodeCustomer = updatedProduct.CodeCustomer;
                    product.SupplierName = updatedProduct.SupplierName;
                    product.SupplierLogo = updatedProduct.SupplierLogo;
                    product.InStock = updatedProduct.InStock;
                    product.CurrencyPrice = updatedProduct.CurrencyPrice;
                    product.PriceNet = updatedProduct.PriceNet;
                    product.PriceGross = updatedProduct.PriceGross;
                    product.DeliveryType = updatedProduct.DeliveryType;
                    product.UpdatedDate = DateTime.UtcNow;

                    var rootBrands = applications
                        .Where(a => a.ParentID == 0)
                        .Select(a => a.Name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .ToList();

                    product.Name = FixName(updatedProduct.Name, updatedProduct.CodeGaska, updatedProduct.CodeCustomer, rootBrands);

                    await _context.BulkUpdateAsync(new List<Product> { product }, ct);

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public async Task<List<Product>> GetProductsWithoutDefaultCategory(CancellationToken ct)
        {
            return await _context.Products
                .Where(p => p.DefaultAllegroCategory == 0 && !p.Archived && p.Categories.Any())
                .ToListAsync(ct);
        }

        public async Task<List<Product>> GetProductsToUpdateParameters(CancellationToken ct)
        {
            // Step 1: products without parameters
            var productsWithoutParameters = await _context.Products
                .AsNoTracking()
                .Include(p => p.CrossNumbers)
                .Include(p => p.Applications)
                .Include(p => p.Parameters.Select(pp => pp.CategoryParameter.Values))
                .Where(p => p.Categories.Any()
                            && !p.Archived
                            && p.DefaultAllegroCategory != 0
                            && !p.Parameters.Any())
                .ToListAsync(ct);

            return productsWithoutParameters;
        }

        public async Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct)
        {
            if (categoryId == 0)
                return;

            // Delete only parameters whose CategoryParameter.CategoryId != new category
            await _context.Database.ExecuteSqlCommandAsync(@"
                DELETE pp
                FROM ProductParameters pp
                INNER JOIN Products p ON pp.ProductId = p.Id
                WHERE pp.ProductId = @p0 AND p.DefaultAllegroCategory <> @p1",
                productId, categoryId);

            // Update the product category directly in the database
            await _context.Database.ExecuteSqlCommandAsync(
                "UPDATE Products SET DefaultAllegroCategory = @p0 WHERE Id = @p1",
                categoryId, productId);
        }

        public async Task UpdateProductAllegroCategory(string productCode, string categoryId, CancellationToken ct)
        {
            var product = await _context.Products
                .Include(p => p.Parameters)
                .Where(p => p.CodeGaska == productCode).FirstOrDefaultAsync(ct);

            if (product != null && categoryId != "0" && product.DefaultAllegroCategory != Convert.ToInt32(categoryId))
            {
                await _context.Database.ExecuteSqlCommandAsync(
                    "DELETE FROM ProductParameters WHERE ProductId = @p0", product.Id);

                product.DefaultAllegroCategory = Convert.ToInt32(categoryId);

                await _context.SaveChangesAsync(ct);
            }
        }

        public async Task<List<Product>> GetProductsToUpload(CancellationToken ct)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-60);
            const int pageSize = 500;
            var result = new List<Product>();

            int page = 0;
            List<Product> batch;

            do
            {
                var productsPage = await _context.Products
                    .AsNoTracking()
                    .Where(p => p.Categories.Any()
                                && !p.Archived
                                && p.DefaultAllegroCategory != 0
                                && p.PriceGross > 1.00m
                                && p.InStock > 0
                                && !p.AllegroOffers.Any()
                                && p.Images.Any(i => !string.IsNullOrEmpty(i.AllegroUrl) && i.AllegroExpirationDate >= cutoff)
                                // Ensure all required parameters exist
                                && _context.CategoryParameters
                                    .Where(cp => cp.CategoryId == p.DefaultAllegroCategory && cp.RequiredForProduct)
                                    .All(cp => p.Parameters.Any(pp => pp.CategoryParameterId == cp.Id))
                    )
                    .OrderBy(p => p.Id)
                    .Skip(page * pageSize)
                    .Take(pageSize)
                    .Select(p => new
                    {
                        Product = p,
                        Images = p.Images
                            .Where(i => !string.IsNullOrEmpty(i.AllegroUrl) && i.AllegroExpirationDate >= cutoff)
                            .GroupBy(i => i.AllegroUrl)
                            .Select(g => g.FirstOrDefault())
                            .ToList()
                    })
                    .ToListAsync(ct);

                batch = productsPage.Select(x =>
                {
                    x.Product.Images = x.Product.Images
                        .Where(i => !string.IsNullOrEmpty(i.AllegroUrl) && i.AllegroExpirationDate >= cutoff)
                        .GroupBy(i => i.AllegroUrl)
                        .Select(g => g.First())
                        .ToList();

                    return x.Product;
                }).ToList();

                result.AddRange(batch);
                page++;
            } while (batch.Any());

            return result;
        }

        public async Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct)
        {
            if (parameters == null || !parameters.Any())
                return;

            const int batchSize = 100;

            // Split parameters into batches
            var parameterBatches = parameters
                .Select((p, index) => new { p, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.p).ToList());

            int batchNumber = 0;
            foreach (var batch in parameterBatches)
            {
                batchNumber++;
                var stopwatchBatch = Stopwatch.StartNew();

                // 1. Get product/category ids
                var productIds = batch.Select(p => p.ProductId).Distinct().ToList();
                var categoryParamIds = batch.Select(p => p.CategoryParameterId).Distinct().ToList();

                // 2. Fetch existing parameters
                var existingParams = _context.ProductParameters
                    .Where(p => productIds.Contains(p.ProductId) && categoryParamIds.Contains(p.CategoryParameterId))
                    .ToList();

                var existingDict = existingParams.ToDictionary(
                    p => (p.ProductId, p.CategoryParameterId)
                );

                var toInsert = new List<ProductParameter>();

                foreach (var param in batch)
                {
                    if (existingDict.TryGetValue((param.ProductId, param.CategoryParameterId), out var existing))
                    {
                        existing.Value = param.Value;
                        existing.IsForProduct = param.IsForProduct;
                    }
                    else
                    {
                        toInsert.Add(param);
                    }
                }

                // 3. Add new parameters in bulk
                if (toInsert.Any())
                    _context.ProductParameters.AddRange(toInsert);

                // 4. Save changes
                await _context.SaveChangesAsync(ct);
            }
        }

        public async Task UpdateParameter(int productId, int parameterId, string value, CancellationToken ct)
        {
            // Get the categoryParameterId
            var categoryId = await _context.Products
                .Where(p => p.Id == productId)
                .Select(p => p.DefaultAllegroCategory)
                .FirstOrDefaultAsync(ct);

            var categoryParameterId = await _context.CategoryParameters
                .Where(cp => cp.ParameterId == parameterId && cp.CategoryId == categoryId)
                .Select(cp => cp.Id)
                .FirstOrDefaultAsync(ct);

            if (categoryParameterId == 0) return;

            // Update Value directly in the database
            await _context.Database.ExecuteSqlCommandAsync(
                "UPDATE ProductParameters SET Value = @p0 WHERE ProductId = @p1 AND CategoryParameterId = @p2",
                value, productId, categoryParameterId);
        }

        public async Task SaveCompatibleProductsAsync(IEnumerable<CompatibleProduct> products, CancellationToken ct = default)
        {
            if (products == null || !products.Any())
                return;

            var ids = products.Select(p => p.Id).ToList();

            var existingProducts = _context.CompatibleProducts
                .Where(p => ids.Contains(p.Id))
                .ToList();

            var existingDict = existingProducts.ToDictionary(p => p.Id);

            var toInsert = new List<CompatibleProduct>();
            var toUpdate = new List<CompatibleProduct>();

            foreach (var product in products)
            {
                if (existingDict.TryGetValue(product.Id, out var existing))
                {
                    existing.Name = product.Name;
                    existing.Type = product.Type;
                    existing.GroupName = product.GroupName;
                    toUpdate.Add(existing);
                }
                else
                {
                    toInsert.Add(product);
                }
            }

            if (toInsert.Any())
                _context.BulkInsert(toInsert);

            if (toUpdate.Any())
                _context.BulkUpdate(toUpdate);
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