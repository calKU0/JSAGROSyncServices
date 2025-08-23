using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Migrations;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Remoting.Contexts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace JSAGROAllegroSync.Data
{
    public class ProductRepository : IProductRepository
    {
        private readonly MyDbContext _context;

        public ProductRepository(MyDbContext context)
        {
            _context = context;
        }

        public async Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds)
        {
            foreach (var apiProduct in apiProducts)
            {
                fetchedProductIds.Add(apiProduct.Id);

                var product = await _context.Products
                    .FirstOrDefaultAsync(p => p.Id == apiProduct.Id);

                if (product == null)
                {
                    product = new Product { Id = apiProduct.Id };
                    _context.Products.Add(product);
                }

                // Map/overwrite properties
                product.CodeGaska = apiProduct.CodeGaska;
                product.CodeCustomer = apiProduct.CodeCustomer;
                product.Name = apiProduct.Name;
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
                product.Archived = false;
            }

            await _context.SaveChangesAsync();
        }

        public async Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds)
        {
            string ids = string.Join(",", fetchedProductIds);
            string sql = $@"
                UPDATE Products
                SET Archived = 1
                WHERE Archived = 0 AND Id NOT IN ({ids})";

            return await _context.Database.ExecuteSqlCommandAsync(sql);
        }

        public async Task<List<Product>> GetProductsForDetailUpdate(int limit)
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
                                        .ToListAsync();

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
                                    .ToListAsync();
            }

            return productsToUpdate;
        }

        public async Task UpdateProductDetails(Product product, ApiProduct updatedProduct)
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
            product.CodeGaska = updatedProduct.CodeGaska;
            product.CodeCustomer = updatedProduct.CodeCustomer;
            product.Name = updatedProduct.Name;
            product.SupplierName = updatedProduct.SupplierName;
            product.SupplierLogo = updatedProduct.SupplierLogo;
            product.InStock = updatedProduct.InStock;
            product.CurrencyPrice = updatedProduct.CurrencyPrice;
            product.PriceNet = updatedProduct.PriceNet;
            product.PriceGross = updatedProduct.PriceGross;
            product.UpdatedDate = DateTime.Now;

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

            await _context.SaveChangesAsync();
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

            if (productsWithoutParameters.Any())
                return productsWithoutParameters;

            return await _context.Products
                .Include(p => p.CrossNumbers)
                .Include(p => p.Applications)
                .Include(p => p.Atributes)
                .Include(p => p.Parameters.Select(pp => pp.CategoryParameter.Values))
                .Where(p => p.Categories.Any()
                            && !p.Archived
                            && p.DefaultAllegroCategory != 0)
                .OrderByDescending(p => p.UpdatedDate)
                .Take(1000)
                .ToListAsync(ct);
        }

        public async Task<List<int>> GetDefaultCategories(CancellationToken ct)
        {
            // 1. Get products with DefaultAllegroCategory and without parameters
            var productsData = await _context.Products
                .Where(p => p.DefaultAllegroCategory != 0
                            && !p.Archived
                            && !_context.CategoryParameters.Any(cp => cp.CategoryId == p.DefaultAllegroCategory))
                .Select(p => new
                {
                    p.DefaultAllegroCategory
                })
                .Distinct()
                .ToListAsync(ct);

            var products = productsData.Select(p => p.DefaultAllegroCategory).ToList();

            return products;
        }

        public async Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct)
        {
            var product = await _context.Products.Where(p => p.Id == productId).FirstOrDefaultAsync(ct);

            if (product != null && categoryId != 0)
            {
                product.DefaultAllegroCategory = categoryId;
                await _context.SaveChangesAsync(ct);
            }
        }

        public async Task<int?> GetMostCommonDefaultAllegroCategory(int productId, CancellationToken ct)
        {
            // 1 Load all categories for this product
            var productCategories = await _context.ProductCategories
                .Where(pc => pc.ProductId == productId)
                .ToListAsync(ct);

            if (!productCategories.Any())
                return null;

            // 2 Find root branches
            var rootBranches = productCategories.Where(pc => pc.ParentID == 0).ToList();

            // Prefer root 19178 ("Części według rodzaju")
            var mainRoot = rootBranches.FirstOrDefault(r => r.CategoryId == 19178)
                           ?? rootBranches.FirstOrDefault();

            if (mainRoot == null)
                return null;

            // 3 Traverse branch to get all categories in this branch
            var branchCategories = productCategories
                .Where(pc => IsInBranch(pc, mainRoot.CategoryId, productCategories))
                .ToList();

            // Leaf = category that is not a ParentID for any other in this branch
            var leaf = branchCategories.FirstOrDefault(c => !branchCategories.Any(other => other.ParentID == c.CategoryId));

            if (leaf != null)
            {
                // 4 Find other products with the same leaf and non-null DefaultAllegroCategory
                var categoryStats = await _context.ProductCategories
                    .Where(pc => pc.CategoryId == leaf.CategoryId
                                 && pc.ProductId != productId
                                 && pc.Product.DefaultAllegroCategory != 0)
                    .GroupBy(pc => pc.Product.DefaultAllegroCategory)
                    .Select(g => new
                    {
                        CategoryId = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(g => g.Count)
                    .FirstOrDefaultAsync(ct);

                if (categoryStats != null)
                    return categoryStats.CategoryId;
            }

            // 5 Fallback: search branch names for 'traktor'
            var traktorMatch = branchCategories.FirstOrDefault(c => c.Name.ToLower().Contains("traktor"));
            if (traktorMatch != null)
            {
                // return Motoryzacja/Części do maszyn i innych pojazdów/Części do maszyn rolniczych/Do traktorów/Części montażowe
                return 305829;
            }

            // 6 Fallback: search branch names for 'kombajn'
            var kombajnMatch = branchCategories.FirstOrDefault(c => c.Name.ToLower().Contains("kombajn"));
            if (kombajnMatch != null)
            {
                // return Motoryzacja/Części do maszyn i innych pojazdów/Części do maszyn rolniczych/Do kombajnów/Części montażowe
                return 319159;
            }

            return null;
        }

        public async Task<Product> GetByIdAsync(int id, CancellationToken ct)
        {
            return await _context.Products
                .Include(p => p.Atributes)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
        }

        public async Task SaveCategoryParametersAsync(IEnumerable<CategoryParameter> parameters, CancellationToken ct)
        {
            foreach (var param in parameters)
            {
                var existing = await _context.CategoryParameters
                    .Include(cp => cp.Values)
                    .FirstOrDefaultAsync(cp => cp.ParameterId == param.ParameterId
                                            && cp.CategoryId == param.CategoryId, ct);

                if (existing != null)
                {
                    existing.Name = param.Name;
                    existing.Type = param.Type;
                    existing.Required = param.Required;
                    existing.Min = param.Min;
                    existing.Max = param.Max;

                    _context.CategoryParameterValues.RemoveRange(existing.Values); // clear old
                    existing.Values = param.Values ?? new List<CategoryParameterValue>();
                }
                else
                {
                    _context.CategoryParameters.Add(param);
                }
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task SetDefaultCategoryAsync(int productId, int categoryId, CancellationToken ct)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId, ct);

            if (product == null)
                throw new InvalidOperationException($"Product {productId} not found.");

            product.DefaultAllegroCategory = categoryId;
            product.UpdatedDate = DateTime.Now;

            await _context.SaveChangesAsync(ct);
        }

        public async Task<List<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct = default)
        {
            return await _context.CategoryParameters
                .Where(cp => cp.CategoryId == categoryId)
                .ToListAsync(ct);
        }

        public async Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct = default)
        {
            foreach (var param in parameters)
            {
                _context.ProductParameters.AddOrUpdate(param);
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task<List<Product>> GetProductsToUpload(CancellationToken ct)
        {
            return await _context.Products
                .Include(p => p.CrossNumbers)
                .Include(p => p.Applications)
                .Include(p => p.Atributes)
                .Include(p => p.Images)
                .Include(p => p.Parameters)
                .Include(p => p.Packages)
                .Where(p => p.Categories.Any() && !p.Archived)
                .Take(10000)
                .ToListAsync(ct);
        }

        public async Task SaveCompatibleProductsAsync(IEnumerable<CompatibleProduct> products, CancellationToken ct = default)
        {
            foreach (var product in products)
            {
                _context.CompatibleProducts.AddOrUpdate(product);
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task<bool> UpdateProductAllegroImage(int imageId, string imageUrl, DateTime expiresAt, CancellationToken ct)
        {
            var image = await _context.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId, ct);
            if (image == null)
                return false;

            image.AllegroUrl = imageUrl;
            image.AllegroExpirationDate = expiresAt;

            var changes = await _context.SaveChangesAsync(ct);
            return changes > 0;
        }

        /// <summary>
        /// Recursively check if a category belongs to branch with given root.
        /// </summary>
        private bool IsInBranch(ProductCategory category, int rootId, List<ProductCategory> allCategories)
        {
            if (category.CategoryId == rootId)
                return true;

            var parent = allCategories.FirstOrDefault(c => c.CategoryId == category.ParentID);
            if (parent == null)
                return false;

            return IsInBranch(parent, rootId, allCategories);
        }

        public async Task<List<ProductImage>> GetImagesForImport(CancellationToken ct)
        {
            return await _context.ProductImages
                .Include(pi => pi.Product)
                .Where(pi => pi.Product.Categories.Any()
                    && pi.Product.DefaultAllegroCategory != 0
                    && (string.IsNullOrEmpty(pi.AllegroUrl) || (pi.AllegroExpirationDate != null && pi.AllegroExpirationDate <= DateTime.UtcNow)))
                .ToListAsync(ct);
        }
    }
}