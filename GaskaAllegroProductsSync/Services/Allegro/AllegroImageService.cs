using GaskaAllegroProductsSync.DTOs.AllegroApi;
using GaskaAllegroProductsSync.Models.Product;
using GaskaAllegroProductsSync.Repositories.Interfaces;
using GaskaAllegroProductsSync.Services.Allegro.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace GaskaAllegroProductsSync.Services.Allegro
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
                var updates = new List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)>();
                var images = await _imageRepo.GetImagesForImport(ct);

                foreach (var image in images)
                {
                    try
                    {
                        var extension = Path.GetExtension(image.Url);
                        if (extension == null)
                            continue;

                        extension = extension.ToLowerInvariant();

                        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
                        {
                            _logger.LogInformation("Skipping unsupported image format: {Url}", image.Url);
                            continue;
                        }

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
                        _logger.LogInformation("Image uploaded for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                    catch (Exception exImage)
                    {
                        _logger.LogError(exImage, "Exception while uploading image for product {Name} ({Code})", image.Product.Name, image.Product.CodeGaska);
                    }
                }

                await _imageRepo.UpdateProductAllegroImages(updates, ct);
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