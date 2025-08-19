using JSAGROAllegroSync.Models;
using System;
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
        public DbSet<ProductParameter> ProductParameters { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<ProductFile> ProductFiles { get; set; }
        public DbSet<ProductCategory> ProductCategories { get; set; }
        public DbSet<AllegroTokenEntity> AllegroTokens { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>()
                .Property(p => p.CreatedDate)
                .HasColumnType("datetime2");

            modelBuilder.Entity<Product>()
                .Property(p => p.UpdatedDate)
                .HasColumnType("datetime2");

            base.OnModelCreating(modelBuilder);
        }

        public override int SaveChanges()
        {
            var entries = ChangeTracker.Entries<Product>();

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.CreatedDate = DateTime.Now;
                    entry.Entity.UpdatedDate = DateTime.Now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.Entity.UpdatedDate = DateTime.Now;
                }
            }

            return base.SaveChanges();
        }
    }
}