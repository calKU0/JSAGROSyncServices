using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using System.Data.Entity;

namespace JSAGROAllegroSync.Data
{
    public class MyDbContext : DbContext
    {
        public MyDbContext() : base("name=MyDbContext")
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Package> Packages { get; set; }
        public DbSet<CrossNumber> CrossNumbers { get; set; }
        public DbSet<Component> Components { get; set; }
        public DbSet<RecommendedPart> RecommendedParts { get; set; }
        public DbSet<Application> Applications { get; set; }
        public DbSet<ProductAttribute> ProductAttributes { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<ProductFile> ProductFiles { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<AllegroTokenEntity> AllegroTokens { get; set; }
        public DbSet<CategoryParameter> CategoryParameters { get; set; }
        public DbSet<CategoryParameterValue> CategoryParameterValues { get; set; }
        public DbSet<ProductParameter> ProductParameters { get; set; }
        public DbSet<CompatibleProduct> CompatibleProducts { get; set; }
        public DbSet<AllegroCategory> AllegroCategories { get; set; }
        public DbSet<AllegroOffer> AllegroOffers { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Product>()
                .Property(p => p.CreatedDate)
                .HasColumnType("datetime2");

            modelBuilder.Entity<Product>()
                .Property(p => p.UpdatedDate)
                .HasColumnType("datetime2");

            modelBuilder.Entity<AllegroOffer>()
                .Property(p => p.StartingAt)
                .HasColumnType("datetime2");

            modelBuilder.Entity<Product>()
                .HasIndex(p => p.CodeGaska)
                .IsUnique();

            modelBuilder.Entity<CategoryParameter>()
                .HasIndex(cp => new { cp.ParameterId, cp.CategoryId })
                .IsUnique();

            modelBuilder.Entity<ProductParameter>()
                .HasRequired(pp => pp.Product)
                .WithMany(p => p.Parameters)
                .HasForeignKey(pp => pp.ProductId)
                .WillCascadeOnDelete(true);

            modelBuilder.Entity<ProductParameter>()
                .HasRequired(pp => pp.CategoryParameter)
                .WithMany()
                .HasForeignKey(pp => pp.CategoryParameterId)
                .WillCascadeOnDelete(false);
        }
    }
}