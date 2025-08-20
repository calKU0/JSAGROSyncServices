using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var incomingIds = apiProducts.Select(p => p.Id).ToList();
                    var existingProducts = await _context.Products
                                            .Where(p => incomingIds.Contains(p.Id))
                                            .ToDictionaryAsync(p => p.Id);

                    foreach (var apiProduct in apiProducts)
                    {
                        fetchedProductIds.Add(apiProduct.Id);

                        if (!existingProducts.TryGetValue(apiProduct.Id, out var product))
                        {
                            product = new Product { Id = apiProduct.Id };
                            _context.Products.Add(product);
                        }

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
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
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
                                        .Where(p => !p.Categories.Any() && !p.Archived)
                                        .Take(limit)
                                        .ToListAsync();

            if (!productsToUpdate.Any())
            {
                productsToUpdate = await _context.Products
                                    .Where(p => !p.Archived)
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
            _context.ProductParameters.RemoveRange(product.Parameters);
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

            product.Parameters = updatedProduct.Parameters.Select(p => new ProductParameter
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

        public async Task<List<ProductDto>> GetProductsToUpload()
        {
            // Step 1: Fetch only the necessary fields and related collections
            var productsData = await _context.Products
                .Where(p => p.Categories.Any() && !p.Archived)
                .Select(p => new
                {
                    p.Id,
                    p.CodeGaska,
                    p.CodeCustomer,
                    p.Name,
                    p.Unit,
                    p.Ean,
                    p.SupplierName,
                    p.SupplierLogo,
                    p.Description,
                    p.TechnicalDetails,
                    p.PriceNet,
                    p.PriceGross,
                    p.CurrencyPrice,
                    p.WeightNet,
                    p.InStock,
                    p.DefaultAllegroCategory,
                    CrossNumbers = p.CrossNumbers.Select(cn => cn.CrossNumberValue).ToList(),
                    Applications = p.Applications.Select(a => new
                    {
                        a.Id,
                        a.ParentID,
                        a.Name
                    }).ToList(),
                    Parameters = p.Parameters.Select(pa => new
                    {
                        pa.Id,
                        pa.AttributeName,
                        pa.AttributeValue
                    }).ToList(),
                    Images = p.Images.Select(i => new
                    {
                        i.Title,
                        i.Url
                    }).ToList()
                })
                .ToListAsync();

            // Step 2: Project into DTOs in memory
            var products = productsData.Select(p => new ProductDto
            {
                Id = p.Id,
                CodeGaska = p.CodeGaska,
                CodeCustomer = p.CodeCustomer,
                Name = p.Name,
                Unit = p.Unit,
                Ean = p.Ean,
                SupplierName = p.SupplierName,
                SupplierLogo = p.SupplierLogo,
                Description = p.Description,
                TechnicalDetails = p.TechnicalDetails,
                NetPrice = p.PriceNet,
                GrossPrice = p.PriceGross,
                CurrencyPrice = p.CurrencyPrice,
                NetWeight = p.WeightNet,
                InStock = p.InStock,
                SuggestedCategoryId = p.DefaultAllegroCategory,
                CrossNumbers = string.Join(",", p.CrossNumbers),
                Applications = p.Applications.Select(a => new ApplicationDto
                {
                    Id = a.Id,
                    ParentId = a.ParentID,
                    Name = a.Name
                }).ToList(),
                Parameters = p.Parameters.Select(pa => new ParameterDto
                {
                    Id = pa.Id,
                    Name = pa.AttributeName,
                    Value = pa.AttributeValue
                }).ToList(),
                Images = p.Images.Select(i => new ImageDto
                {
                    Title = i.Title,
                    Url = i.Url
                }).ToList()
            }).ToList();

            return products;
        }

        public async Task UpdateProductAllegroCategory(int productId, int categoryId)
        {
            var product = await _context.Products.Where(p => p.Id == productId).FirstOrDefaultAsync();

            if (product != null && categoryId != 0)
            {
                product.DefaultAllegroCategory = categoryId;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<int?> GetMostCommonDefaultAllegroCategoryAsync(int productId, CancellationToken ct)
        {
            // Get this product’s first/main category (branch)
            var mainCategoryId = await _context.ProductCategories
                .Where(pc => pc.ProductId == productId && pc.ParentID == 0) // <-- adjust rule for "first branch"
                .Select(pc => pc.CategoryId)
                .FirstOrDefaultAsync(ct);

            if (mainCategoryId == 0) // no main category
                return null;

            // Find other products that share the same first branch and have DefaultAllegroCategory set
            var categoryStats = await _context.ProductCategories
                .Where(pc => pc.ParentID == 0 && pc.CategoryId == mainCategoryId && pc.Product.DefaultAllegroCategory != 0)
                .GroupBy(pc => pc.Product.DefaultAllegroCategory)
                .Select(g => new
                {
                    CategoryId = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Count)
                .FirstOrDefaultAsync(ct);

            return categoryStats?.CategoryId;
        }
    }
}