﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroSync.Models.Product
{
    public class ProductImage
    {
        [Key]
        public int Id { get; set; }

        public string Title { get; set; }
        public string Url { get; set; }
        public string AllegroUrl { get; set; }
        public string AllegroLogoUrl { get; set; }

        [Column(TypeName = "datetime2")]
        public DateTime AllegroExpirationDate { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }

        public virtual Product Product { get; set; }
    }
}