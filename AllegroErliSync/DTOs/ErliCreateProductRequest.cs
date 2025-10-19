using System.Collections.Generic;

namespace AllegroErliSync.DTOs
{
    public class ErliCreateProductRequest
    {
        public string Name { get; set; }
        public ErliDescription Description { get; set; }
        public string Ean { get; set; }
        public string Sku { get; set; }
        public List<ErliExternalReference> ExternalReferences { get; set; } = new List<ErliExternalReference>();
        public List<ErliAttribute> ExternalAttributes { get; set; } = new List<ErliAttribute>();
        public List<ErliCategory> ExternalCategories { get; set; } = new List<ErliCategory>();
        public List<ErliImage> Images { get; set; } = new List<ErliImage>();
        public int Price { get; set; }
        public string ReferencePriceType { get; set; }
        public int Stock { get; set; }
        public string Status { get; set; }
        public string DeliveryPriceList { get; set; }
        public ErliDispatchTime DispatchTime { get; set; }
        public int Weight { get; set; }
        public string InvoiceType { get; set; }
        public ErliResponsiblePerson ExternalResponsiblePerson { get; set; }
        public ErliResponsibleProducer ExternalResponsibleProducer { get; set; }
    }

    public class ErliResponsiblePerson
    {
        public string ExternalId { get; set; }
        public string Source { get; set; }
    }

    public class ErliResponsibleProducer
    {
        public string ExternalId { get; set; }
        public string Source { get; set; }
    }

    public class ErliDescription
    {
        public List<ErliDescriptionSection> Sections { get; set; } = new List<ErliDescriptionSection>();
    }

    public class ErliDescriptionSection
    {
        public List<ErliDescriptionItem> Items { get; set; } = new List<ErliDescriptionItem>();
    }

    public class ErliDescriptionItem
    {
        public string Type { get; set; } // TEXT or IMAGE
        public string Content { get; set; }
        public string Url { get; set; }
    }

    public class ErliExternalReference
    {
        public string Id { get; set; }
        public string Kind { get; set; }
    }

    public class ErliAttribute
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string Type { get; set; }
        public List<object> Values { get; set; } = new List<object>();
    }

    public class ErliCategory
    {
        public string Source { get; set; }
        public List<ErliCategoryBreadcrumb> Breadcrumb { get; set; } = new List<ErliCategoryBreadcrumb>();
    }

    public class ErliCategoryBreadcrumb
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class ErliImage
    {
        public string Url { get; set; }
    }

    public class ErliDispatchTime
    {
        public int Period { get; set; }
    }
}