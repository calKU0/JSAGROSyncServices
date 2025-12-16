using JSAGROSyncServices.Shared.DTOs.Allegro;
using JSAGROSyncServices.Shared.Services;
using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Services.Interfaces;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using Image = SixLabors.ImageSharp.Image;

namespace RolmarAllegroProductsSyncService.Services.Allegro
{
    public class AllegroImageService : IAllegroImageService
    {
        private readonly ILogger<AllegroImageService> _logger;
        private readonly AllegroApiClient _apiClient;

        public AllegroImageService(AllegroApiClient apiClient, ILogger<AllegroImageService> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<List<OfferImage>> ImportImages(List<OfferImage> images, CancellationToken ct = default)
        {
            var resultImages = new List<OfferImage>();

            try
            {
                if (images == null || !images.Any())
                {
                    _logger.LogInformation("No images to import.");
                    return resultImages;
                }

                // Preload logo bytes once
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

                var updates = new ConcurrentBag<OfferImage>();

                await Parallel.ForEachAsync(images, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = 10
                },
                async (image, token) =>
                {
                    try
                    {
                        var imageBytes = EnsureMinSize(image);
                        if (imageBytes == null)
                        {
                            _logger.LogWarning("Image too small or invalid {Name}", image.Name);
                            return;
                        }

                        // Upload to Allegro
                        var uploadResult = await _apiClient.PostAsync<AllegroImageResponse>("/sale/images", imageBytes, token, "image/jpeg");
                        if (uploadResult == null || string.IsNullOrWhiteSpace(uploadResult.Location))
                        {
                            _logger.LogWarning("Invalid upload result for {Name}", image.Name);
                            return;
                        }

                        // Assign URLs
                        image.Url = uploadResult.Location;
                        image.LogoUrl = logoUrl;

                        updates.Add(image); // add successfully uploaded image

                        _logger.LogInformation("Image uploaded {Name} -> {Url}", image.Name, image.Url);
                    }
                    catch (Exception exImage)
                    {
                        _logger.LogError(exImage, "Error uploading image {Name}", image.Name);
                    }
                });

                resultImages = updates.ToList();

                _logger.LogInformation("Imported {Count} images successfully.", resultImages.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error while importing images to Allegro.");
            }

            return resultImages;
        }

        private byte[] EnsureMinSize(OfferImage image, int minWidth = 400, int minHeight = 400)
        {
            try
            {
                using var imageTemp = Image.Load(image.Data);

                if (imageTemp.Width >= minWidth && imageTemp.Height >= minHeight)
                    return image.Data; // already large enough

                // Calculate scale factor to meet minimum size
                double scaleX = (double)minWidth / imageTemp.Width;
                double scaleY = (double)minHeight / imageTemp.Height;
                double scale = Math.Max(scaleX, scaleY);

                int newWidth = (int)(imageTemp.Width * scale);
                int newHeight = (int)(imageTemp.Height * scale);

                imageTemp.Mutate(x => x.Resize(newWidth, newHeight));

                using var ms = new MemoryStream();
                imageTemp.Save(ms, new JpegEncoder());
                _logger.LogInformation("Resized image {Name} to {Width}x{Height}px", image.Name, newWidth, newHeight);

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resize image {Name}", image.Name);
                return null;
            }
        }
    }
}