using GaskaAllegroSync.Data;
using GaskaAllegroSync.DTOs.AllegroApi;
using GaskaAllegroSync.Models.Product;
using GaskaAllegroSync.Repositories.Interfaces;
using GaskaAllegroSync.Services.AllegroApi.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Services.AllegroApi
{
    public class AllegroImageService : IAllegroImageService
    {
        private readonly AllegroApiClient _apiClient;
        private readonly IImageRepository _imageRepo;
        private readonly HttpClient _httpClient = new HttpClient();

        public AllegroImageService(IImageRepository imageRepo, AllegroApiClient apiClient)
        {
            _imageRepo = imageRepo;
            _apiClient = apiClient;
        }

        public async Task ImportImages(CancellationToken ct = default)
        {
            try
            {
                var updates = new List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)>();
                var images = await _imageRepo.GetImagesForImport(ct);

                foreach (var image in images)
                {
                    try
                    {
                        string logoUrl = string.Empty;
                        try
                        {
                            var logoImageBytes = File.ReadAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "jsagro-logo.jpg"));
                            var logoResult = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", logoImageBytes, ct, "image/jpeg");
                            logoUrl = logoResult?.Location;
                        }
                        catch
                        {
                        }

                        // Download image bytes
                        var imageBytes = await DownloadImageAsync(image.Url, image.Product.CodeGaska, ct);
                        if (imageBytes == null) continue;

                        imageBytes = EnsureMinSize(imageBytes, image.Product);
                        if (imageBytes == null) continue;

                        // Upload to Allegro
                        var result = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", imageBytes, ct, "image/jpeg");
                        if (result == null || !DateTime.TryParse(result.ExpiresAt, out var expiresAt))
                            continue;

                        // Save to DB
                        updates.Add((image.Id, result.Location, logoUrl, expiresAt));
                        Log.Information("Image uploaded for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                    catch (Exception exImage)
                    {
                        Log.Error(exImage, "Exception while uploading image for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                }

                await _imageRepo.UpdateProductAllegroImages(updates, ct);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while importing images to Allegro.");
            }
        }

        private async Task<byte[]> DownloadImageAsync(string url, string codeGaska, CancellationToken ct)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("Failed to download image from {Url} for product {CodeGaska}", url, codeGaska);
                    return null;
                }

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception while downloading image for product {CodeGaska} from {Url}", codeGaska, url);
                return null;
            }
        }

        private byte[] EnsureMinSize(byte[] imageBytes, Product product, int minWidth = 400, int minHeight = 400)
        {
            try
            {
                using (var ms = new MemoryStream(imageBytes))
                using (var bitmap = new Bitmap(ms))
                {
                    if (bitmap.Width >= minWidth && bitmap.Height >= minHeight)
                    {
                        return imageBytes; // already large enough
                    }

                    // Calculate scale factor to meet minimum size
                    double scaleX = (double)minWidth / bitmap.Width;
                    double scaleY = (double)minHeight / bitmap.Height;
                    double scale = Math.Max(scaleX, scaleY); // scale proportionally

                    int newWidth = (int)(bitmap.Width * scale);
                    int newHeight = (int)(bitmap.Height * scale);

                    using (var newBitmap = new Bitmap(newWidth, newHeight))
                    using (var g = Graphics.FromImage(newBitmap))
                    {
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                        g.DrawImage(bitmap, 0, 0, newWidth, newHeight);

                        using (var outStream = new MemoryStream())
                        {
                            newBitmap.Save(outStream, System.Drawing.Imaging.ImageFormat.Jpeg);
                            Log.Information("Resized image for product {Name} ({Code}) to {Width}x{Height}px", product.Name, product.CodeGaska, newWidth, newHeight);

                            return outStream.ToArray();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to resize image for product {Name} ({Code})", product.Name, product.CodeGaska);
                return null;
            }
        }
    }
}