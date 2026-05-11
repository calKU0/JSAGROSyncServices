using Allegro.JSAGRO.Erli.ProductsService.DTOs;
using JSAGROSyncServices.Contracts.Models;
using Newtonsoft.Json;

namespace Allegro.JSAGRO.Erli.ProductsService.Mappers
{
    public static class ErliProductMapper
    {
        public static ErliCreateProductRequest MapFromOffer(AllegroOffer offer)
        {
            if (offer == null) throw new ArgumentNullException(nameof(offer));

            var attributes = offer.Attributes?.Select(attr =>
            {
                var values = new List<object>();

                if (!string.IsNullOrEmpty(attr.ValuesJson))
                {
                    var valueNames = JsonConvert.DeserializeObject<List<string>>(attr.ValuesJson);
                    var valueIds = string.IsNullOrEmpty(attr.ValuesIdsJson)
                        ? new List<string>()
                        : JsonConvert.DeserializeObject<List<string>>(attr.ValuesIdsJson);

                    if (string.Equals(attr.Type, "dictionary", StringComparison.OrdinalIgnoreCase))
                    {
                        for (int i = 0; i < valueNames.Count; i++)
                        {
                            var id = i < valueIds.Count ? valueIds[i] : valueNames[i];
                            values.Add(new { id, name = valueNames[i] });
                        }
                    }
                    else
                    {
                        values.AddRange(valueNames);
                    }
                }

                string type;
                if (string.IsNullOrWhiteSpace(attr.Type))
                {
                    type = "string";
                }
                else if (attr.Type.Equals("float", StringComparison.OrdinalIgnoreCase))
                {
                    type = "number";
                }
                else
                {
                    type = attr.Type.ToLower();
                }

                return new ErliAttribute
                {
                    Id = attr.AttributeId,
                    Source = "allegro",
                    Type = type,
                    Values = values
                };
            }).ToList() ?? new List<ErliAttribute>();

            var priceInCents = (int)(offer.Price * 100);
            if (IsCourierDelivery(offer.DeliveryName))
            {
                priceInCents += 300;
            }

            var productRequest = new ErliCreateProductRequest
            {
                Name = offer.Name,
                Ean = offer.ExternalId,
                Sku = offer.ExternalId,
                ExternalCategories = new List<ErliCategory>
                {
                    new ErliCategory
                    {
                        Source = "allegro",
                        Breadcrumb = new List<ErliCategoryBreadcrumb>
                        {
                            new ErliCategoryBreadcrumb
                            {
                                Id = offer.CategoryId.ToString(),
                            }
                        },
                    }
                },
                ExternalReferences = new List<ErliExternalReference>
                {
                    new ErliExternalReference
                    {
                        Id = offer.Id,
                        Kind = "allegro",
                    }
                },
                ExternalAttributes = attributes,
                Price = priceInCents,
                Stock = offer.Stock,
                Status = offer.Status.ToLower() == "ended" ? "inactive" : offer.Status.ToLower(),
                DispatchTime = DispatchTimeMapper.MapFromHandlingTime(offer.HandlingTime),
                Images = string.IsNullOrWhiteSpace(offer.Images)
                    ? new List<ErliImage>()
                    : JsonConvert.DeserializeObject<List<string>>(offer.Images)
                        .Where(url => !string.IsNullOrWhiteSpace(url))
                        .Select(url => new ErliImage { Url = url })
                        .ToList(),
                Weight = (int)(offer.Weight * 1000),
                InvoiceType = "vatInvoice",
                DeliveryPriceList = offer.DeliveryName,
                ExternalResponsiblePerson = !string.IsNullOrEmpty(offer.ResponsiblePerson)
                    ? new List<ErliResponsiblePerson>
                    {
                        new ErliResponsiblePerson
                        {
                            ExternalId = offer.ResponsiblePerson,
                            Source = "allegro"
                        }
                    }
                    : null,

                ExternalResponsibleProducer = !string.IsNullOrEmpty(offer.ResponsibleProducer)
                    ? new List<ErliResponsibleProducer>
                    {   new ErliResponsibleProducer
                        {
                        ExternalId = offer.ResponsibleProducer,
                        Source = "allegro"
                    }
                    }
                    : null
            };

            // Build description
            var descriptions = offer.Descriptions ?? new List<AllegroOfferDescription>();
            var descriptionItems = descriptions
                .Where(d => d != null)
                .OrderBy(d => d.SectionId)
                .ThenBy(d => d.Id)
                .Select(d => new
                {
                    d.SectionId,
                    Item = new ErliDescriptionItem
                    {
                        Type = string.Equals(d.Type, "IMAGE", StringComparison.OrdinalIgnoreCase) ? "IMAGE" : "TEXT",
                        Content = string.Equals(d.Type, "TEXT", StringComparison.OrdinalIgnoreCase) ? d.Content : null,
                        Url = string.Equals(d.Type, "IMAGE", StringComparison.OrdinalIgnoreCase) ? d.Content : null
                    }
                })
                .ToList();

            var sections = descriptionItems
                .GroupBy(x => x.SectionId)
                .OrderBy(g => g.Key)
                .Select(g => new ErliDescriptionSection
                {
                    Items = g.Select(x => x.Item).ToList()
                })
                .ToList();

            productRequest.Description = new ErliDescription { Sections = sections };

            return productRequest;
        }

        private static bool IsCourierDelivery(string? deliveryName)
        {
            if (string.IsNullOrWhiteSpace(deliveryName))
                return false;

            var normalized = deliveryName.Trim();

            return !normalized.Contains("paczkomat", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("paczko", StringComparison.OrdinalIgnoreCase)
                && !normalized.Contains("punkt", StringComparison.OrdinalIgnoreCase);
        }
    }
}