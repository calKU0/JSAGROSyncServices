using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Migrations;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Remoting.Contexts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

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
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            foreach (var id in fetchedProductIds)
                table.Rows.Add(id);

            var param = new SqlParameter("@Ids", SqlDbType.Structured)
            {
                TypeName = "dbo.IntListType",
                Value = table
            };

            string sql = @"
                UPDATE Products
                SET Archived = 1
                WHERE Archived = 0
                AND Id NOT IN (SELECT Id FROM @Ids)";

            return await _context.Database.ExecuteSqlCommandAsync(sql, param, ct);
        }

        public async Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds, CancellationToken ct)
        {
            foreach (var apiProduct in apiProducts)
            {
                fetchedProductIds.Add(apiProduct.Id);
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == apiProduct.Id, ct) ?? new Product { Id = apiProduct.Id };

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
                product.Archived = false;

                if (product.Id == 0) _context.Products.Add(product);
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

        public async Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct)
        {
            var product = await _context.Products.Where(p => p.Id == productId).FirstOrDefaultAsync(ct);

            if (product != null && categoryId != 0)
            {
                product.DefaultAllegroCategory = categoryId;
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
                            && !p.AllegroOffers.Any()
                            )
                .ToListAsync(ct);

            foreach (var product in products)
            {
                product.Images = product.Images.Where(i => i.AllegroUrl != null).ToList();
            }

            return products;
        }

        public async Task<List<Product>> GetProductsToUpdateOffer(string apiDeliveryName, CancellationToken ct)
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
                            && p.DefaultAllegroCategory != 0
                            && p.PriceGross > 1.00m
                            && p.Images.Any(i => i.AllegroUrl != null)
                            && p.AllegroOffers.LastOrDefault() != null
                            && p.AllegroOffers.LastOrDefault().DeliveryName == apiDeliveryName)
                .ToListAsync(ct);

            foreach (var product in products)
            {
                product.Images = product.Images.Where(i => i.AllegroUrl != null).ToList();
            }

            return products;
        }

        public async Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct)
        {
            foreach (var param in parameters)
            {
                _context.ProductParameters.AddOrUpdate(param);
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task SaveCompatibleProductsAsync(IEnumerable<CompatibleProduct> products, CancellationToken ct = default)
        {
            foreach (var product in products)
            {
                _context.CompatibleProducts.AddOrUpdate(product);
            }

            await _context.SaveChangesAsync(ct);
        }

        private string FixName(string name, string code, string supplierName)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                // 1. Remove JAG variants (JAG, JAG16-0034, JAG124, etc.)
                bool jagRemoved = false;
                name = Regex.Replace(
                    name,
                    @"\bJAG(?=[0-9\-])[\w\-]*",
                    m =>
                    {
                        jagRemoved = true;
                        return "";
                    },
                    RegexOptions.IgnoreCase
                );

                // Collapse multiple spaces
                name = Regex.Replace(name, @"\s+", " ").Trim();

                // 2. Move leading product code (if exists) to the end
                var codeMatch = Regex.Match(name, @"^(?<code>\d+[0-9A-Za-z./x-]*)\s+(?<rest>.+)$");
                if (codeMatch.Success)
                {
                    name = $"{codeMatch.Groups["rest"].Value} {codeMatch.Groups["code"].Value}";
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