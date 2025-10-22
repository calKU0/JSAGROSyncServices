using AllegroGaskaProductsSyncService.DTOs.AllegroApi;
using AllegroGaskaProductsSyncService.Models.Product;
using AllegroGaskaProductsSyncService.Repositories.Interfaces;
using AllegroGaskaProductsSyncService.Services.Allegro.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;

namespace AllegroGaskaProductsSyncService.Services.Allegro
{
    public class AllegroImageService : IAllegroImageService
    {
        private readonly ILogger<AllegroImageService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IImageRepository _imageRepo;
        private readonly HttpClient _httpClient = new HttpClient();

        public AllegroImageService(IImageRepository imageRepo, AllegroApiClient apiClient, ILogger<AllegroImageService> logger)
        {
            _imageRepo = imageRepo;
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task ImportImages(CancellationToken ct = default)
        {
            try
            {
                var images = await _imageRepo.GetImagesForImport(ct);
                if (images == null || !images.Any())
                {
                    _logger.LogInformation("No images to import.");
                    return;
                }

                // Preload logo bytes once, not per image
                byte[]? logoImageBytes = null;
                string logoUrl = string.Empty;
                try
                {
                    var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Images", "jsagro-logo.jpg");
                    if (File.Exists(logoPath))
                    {
                        logoImageBytes = File.ReadAllBytes(logoPath);
                        var logoResult = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", logoImageBytes, ct, "image/jpeg");
                        logoUrl = logoResult?.Location ?? string.Empty;
                    }
                }
                catch (Exception exLogo)
                {
                    _logger.LogWarning(exLogo, "Failed to upload or load logo image for Allegro.");
                }

                var updates = new ConcurrentBag<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)>();

                await Parallel.ForEachAsync(images, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = 10
                },
                async (image, token) =>
                {
                    try
                    {
                        var extension = Path.GetExtension(image.Url)?.ToLowerInvariant();
                        if (extension is not (".jpg" or ".jpeg" or ".png"))
                        {
                            _logger.LogDebug("Skipping unsupported image format: {Url}", image.Url);
                            return;
                        }

                        // Download
                        var imageBytes = await DownloadImageAsync(image.Url, image.Product.CodeGaska, token);
                        if (imageBytes == null)
                        {
                            _logger.LogWarning("Failed to download image for {Code}", image.Product.CodeGaska);
                            return;
                        }

                        imageBytes = EnsureMinSize(imageBytes, image.Product);
                        if (imageBytes == null)
                        {
                            _logger.LogWarning("Image too small or invalid for {Code}", image.Product.CodeGaska);
                            return;
                        }

                        // Upload to Allegro
                        var result = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", imageBytes, token, "image/jpeg");
                        if (result == null || !DateTime.TryParse(result.ExpiresAt, out var expiresAt))
                        {
                            _logger.LogWarning("Invalid upload result for {Code}", image.Product.CodeGaska);
                            return;
                        }

                        // Collect for DB update
                        updates.Add((image.Id, result.Location, logoUrl, expiresAt));
                        _logger.LogInformation("Image uploaded for {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                    catch (Exception exImage)
                    {
                        _logger.LogError(exImage, "Error uploading image for {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                });

                // Batch update DB once all are done
                if (updates.Any())
                    await _imageRepo.UpdateProductAllegroImages(updates.ToList(), ct);

                _logger.LogInformation("Imported {Count} images successfully.", updates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while importing images to Allegro.");
            }
        }

        private async Task<byte[]> DownloadImageAsync(string url, string codeGaska, CancellationToken ct)
        {
            try
            {
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to download image from {Url} for product {CodeGaska}", url, codeGaska);
                    return null;
                }

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while downloading image for product {CodeGaska} from {Url}", codeGaska, url);
                return null;
            }
        }

        private byte[] EnsureMinSize(byte[] imageBytes, Product product, int minWidth = 400, int minHeight = 400)
        {
            try
            {
                using var image = Image.Load(imageBytes);

                if (image.Width >= minWidth && image.Height >= minHeight)
                    return imageBytes; // already large enough

                // Calculate scale factor to meet minimum size
                double scaleX = (double)minWidth / image.Width;
                double scaleY = (double)minHeight / image.Height;
                double scale = Math.Max(scaleX, scaleY);

                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);

                image.Mutate(x => x.Resize(newWidth, newHeight));

                using var ms = new MemoryStream();
                image.Save(ms, new JpegEncoder());
                _logger.LogInformation("Resized image for product {Name} ({Code}) to {Width}x{Height}px", product.Name, product.CodeGaska, newWidth, newHeight);

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resize image for product {Name} ({Code})", product.Name, product.CodeGaska);
                return null;
            }
        }
    }
}