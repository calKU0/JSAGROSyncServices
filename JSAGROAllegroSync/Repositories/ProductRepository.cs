using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories
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
                .Include(p => p.Packages)
                .Include(p => p.CrossNumbers)
                .Include(p => p.Components)
                .Include(p => p.RecommendedParts)
                .Include(p => p.Applications)
                .Include(p => p.Atributes)
                .Include(p => p.Images)
                .Include(p => p.Files)
                .Include(p => p.Categories)
                .Where(p => !p.Categories.Any() && !p.Archived)
                .Take(limit)
                .ToListAsync(ct);

            if (!productsToUpdate.Any())
            {
                productsToUpdate = await _context.Products
                    .Where(p => !p.Archived)
                    .Include(p => p.Packages)
                    .Include(p => p.CrossNumbers)
                    .Include(p => p.Components)
                    .Include(p => p.RecommendedParts)
                    .Include(p => p.Applications)
                    .Include(p => p.Atributes)
                    .Include(p => p.Images)
                    .Include(p => p.Files)
                    .Include(p => p.Categories)
                    .OrderBy(p => p.UpdatedDate)
                    .Take(limit)
                    .ToListAsync(ct);
            }

            return productsToUpdate;
        }

        public async Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            string ids = string.Join(",", fetchedProductIds);
            string sql = $@"
                UPDATE Products
                SET Archived = 1
                WHERE Archived = 0 AND Id NOT IN ({ids})";

            var prevTimeout = _context.Database.CommandTimeout;
            try
            {
                _context.Database.CommandTimeout = 240;
                return await _context.Database.ExecuteSqlCommandAsync(sql, ct);
            }
            finally
            {
                _context.Database.CommandTimeout = prevTimeout;
            }
        }

        public async Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            foreach (var apiProduct in apiProducts)
            {
                fetchedProductIds.Add(apiProduct.Id);

                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Id == apiProduct.Id, ct);

                if (product == null)
                {
                    product = new Product
                    {
                        Id = apiProduct.Id
                    };
                    _context.Products.Add(product);
                }

                product.Name = FixName(apiProduct.Name, apiProduct.CodeGaska, apiProduct.CodeCustomer);
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

            await _context.SaveChangesAsync(ct);
        }

        public async Task UpdateProductDetails(Product product, ApiProduct updatedProduct, CancellationToken ct)
        {
            // Clear existing collections
            _context.Packages.RemoveRange(product.Packages);
            _context.CrossNumbers.RemoveRange(product.CrossNumbers);
            _context.Components.RemoveRange(product.Components);
            _context.RecommendedParts.RemoveRange(product.RecommendedParts);
            _context.Applications.RemoveRange(product.Applications);
            _context.ProductAttributes.RemoveRange(product.Atributes);
            _context.ProductImages.RemoveRange(product.Images);
            _context.ProductFiles.RemoveRange(product.Files);
            _context.ProductCategories.RemoveRange(product.Categories);

            // Update fields
            product.Name = FixName(updatedProduct.Name, updatedProduct.CodeGaska, updatedProduct.CodeCustomer);
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

            // Map collections
            product.Packages = updatedProduct.Packages.Select(p => new Package
            {
                PackUnit = p.PackUnit,
                PackQty = p.PackQty,
                PackNettWeight = p.PackNettWeight,
                PackGrossWeight = p.PackGrossWeight,
                PackEan = p.PackEan,
                PackRequired = p.PackRequired
            }).ToList();

            product.CrossNumbers = (updatedProduct.CrossNumbers ?? Enumerable.Empty<ApiCrossNumber>())
                .Where(c => c != null && !string.IsNullOrEmpty(c.CrossNumber))
                .SelectMany(c => c.CrossNumber.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(cn => new CrossNumber
                    {
                        CrossNumberValue = cn.Trim(),
                        CrossManufacturer = c.CrossManufacturer
                    }))
                .ToList();

            product.Components = updatedProduct.Components.Select(c => new Component
            {
                TwrID = c.TwrID,
                CodeGaska = c.CodeGaska,
                Qty = c.Qty
            }).ToList();

            product.RecommendedParts = updatedProduct.RecommendedParts.Select(r => new RecommendedPart
            {
                TwrID = r.TwrID,
                CodeGaska = r.CodeGaska,
                Qty = r.Qty
            }).ToList();

            product.Applications = updatedProduct.Applications.Select(a => new Application
            {
                ApplicationId = a.Id,
                ParentID = a.ParentID,
                Name = a.Name
            }).ToList();

            product.Atributes = updatedProduct.Parameters.Select(p => new ProductAttribute
            {
                AttributeId = p.AttributeId,
                AttributeName = p.AttributeName,
                AttributeValue = p.AttributeValue
            }).ToList();

            product.Images = updatedProduct.Images.Select(i => new ProductImage
            {
                Title = i.Title,
                Url = i.Url
            }).ToList();

            product.Files = updatedProduct.Files.Select(f => new ProductFile
            {
                Title = f.Title,
                Url = f.Url
            }).ToList();

            product.Categories = updatedProduct.Categories.Select(c => new ProductCategory
            {
                CategoryId = c.Id,
                ParentID = c.ParentID,
                Name = c.Name
            }).ToList();

            await _context.SaveChangesAsync(ct);
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
                .Include(p => p.CrossNumbers)
                .Include(p => p.Applications)
                .Include(p => p.Atributes)
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
                INNER JOIN CategoryParameters cp ON pp.CategoryParameterId = cp.Id
                WHERE pp.ProductId = @p0 AND cp.CategoryId <> @p1",
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
            var products = await _context.Products
                .Include(p => p.CrossNumbers)
                .Include(p => p.Applications)
                .Include(p => p.Atributes)
                .Include(p => p.Images)
                .Include(p => p.Parameters)
                .Include(p => p.Packages)
                .Include(p => p.AllegroOffers)
                .Where(p => p.Categories.Any()
                            && !p.Archived
                            && p.DefaultAllegroCategory != 0
                            && p.PriceGross > 1.00m
                            && p.InStock > 0
                            && p.Images.Any(i => i.AllegroUrl != null)
                            && !p.AllegroOffers.Any())
                .ToListAsync(ct);

            foreach (var product in products)
            {
                product.Images = product.Images.Where(i => i.AllegroUrl != null).ToList();
            }

            return products;
        }

        public async Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct)
        {
            if (parameters == null || !parameters.Any())
                return;

            const int batchSize = 50;

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

            const int batchSize = 500; // Adjust batch size

            var productList = products.ToList();

            var stopwatchTotal = Stopwatch.StartNew();

            var batches = productList
                .Select((p, index) => new { p, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.p).ToList());

            int batchNumber = 0;
            foreach (var batch in batches)
            {
                batchNumber++;
                var stopwatchBatch = Stopwatch.StartNew();

                var ids = batch.Select(p => p.Id).ToList();

                // Fetch existing products in this batch
                var existingProducts = _context.CompatibleProducts
                    .Where(p => ids.Contains(p.Id))
                    .ToList();

                var existingDict = existingProducts.ToDictionary(p => p.Id);

                var toInsert = new List<CompatibleProduct>();

                foreach (var product in batch)
                {
                    if (existingDict.TryGetValue(product.Id, out var existing))
                    {
                        // Update fields
                        existing.Name = product.Name;
                        existing.Type = product.Type;
                        existing.GroupName = product.GroupName;
                    }
                    else
                    {
                        toInsert.Add(product);
                    }
                }

                // Bulk insert
                if (toInsert.Any())
                    _context.CompatibleProducts.AddRange(toInsert);

                await _context.SaveChangesAsync(ct);

                stopwatchBatch.Stop();
                Console.WriteLine($"Batch {batchNumber} saved in {stopwatchBatch.ElapsedMilliseconds} ms");
            }

            stopwatchTotal.Stop();
            Console.WriteLine($"Total save time: {stopwatchTotal.ElapsedMilliseconds} ms");
        }

        private string FixName(string name, string code, string supplierName)
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

                // 2. Only move leading product code from original name if it wasn't a JAG variant
                if (!jagRemoved && !string.IsNullOrWhiteSpace(code))
                {
                    var match = Regex.Match(name, @"^(?<code>[0-9A-Za-z./x-]+)\s+(?<rest>.+)$");

                    if (match.Success)
                    {
                        name = $"{match.Groups["rest"].Value} {match.Groups["code"].Value}";
                    }
                }

                // 3. Append CodeGaska
                bool codeGaskaAppended = false;
                var words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if ((jagRemoved || words.Length < 3) && !string.IsNullOrWhiteSpace(code))
                {
                    name = $"{name} {code}".Trim();
                    codeGaskaAppended = true;
                    words = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                }

                // 4. If CodeGaska is in the name AND <3 words → add SupplierName or 'a'
                if (codeGaskaAppended && words.Length < 3)
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