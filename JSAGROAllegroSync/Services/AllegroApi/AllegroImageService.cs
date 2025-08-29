using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using JSAGROAllegroSync.Services.AllegroApi.Interfaces;
using Serilog;
using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi
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

                        // Skip small images
                        if (!IsLargeEnough(imageBytes, image.Product))
                            continue;

                        // Upload to Allegro
                        var result = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", imageBytes, ct, "image/jpeg");
                        if (result == null || !DateTime.TryParse(result.ExpiresAt, out var expiresAt))
                        {
                            Log.Warning("Couldn't parse Allegro expiration date for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                            continue;
                        }

                        // Save to DB
                        var success = await _imageRepo.UpdateProductAllegroImage(image.Id, result.Location, logoUrl, expiresAt, ct);
                        if (success)
                            Log.Information("Image uploaded for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                        else
                            Log.Error("Couldn't save uploaded image data in database for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                    catch (Exception exImage)
                    {
                        Log.Error(exImage, "Exception while uploading image for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                }
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

        private bool IsLargeEnough(byte[] imageBytes, Product product)
        {
            try
            {
                var ms = new MemoryStream(imageBytes);
                var bitmap = new Bitmap(ms);
                if (bitmap.Width < 400 && bitmap.Height < 400)
                {
                    Log.Information("Skipping image upload for product {Name} ({CodeGaska}) because it is too small: {Width}x{Height}px", product.Name, product.CodeGaska, bitmap.Width, bitmap.Height);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check image size for product {Name} ({CodeGaska})", product.Name, product.CodeGaska);
                return false;
            }
        }
    }
}