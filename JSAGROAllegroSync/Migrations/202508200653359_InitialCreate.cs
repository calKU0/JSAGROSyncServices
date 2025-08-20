namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class InitialCreate : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.AllegroTokenEntities",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        AccessToken = c.String(),
                        RefreshToken = c.String(),
                        ExpiryDateUtc = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "dbo.Applications",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        ApplicationId = c.Int(nullable: false),
                        ParentID = c.Int(nullable: false),
                        Name = c.String(),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.Products",
                c => new
                    {
                        Id = c.Int(nullable: false),
                        CodeGaska = c.String(),
                        CodeCustomer = c.String(),
                        Name = c.String(),
                        Description = c.String(),
                        Ean = c.String(),
                        TechnicalDetails = c.String(),
                        WeightNet = c.Single(nullable: false),
                        WeightGross = c.Single(nullable: false),
                        SupplierName = c.String(),
                        SupplierLogo = c.String(),
                        InStock = c.Single(nullable: false),
                        CurrencyPrice = c.String(),
                        PriceNet = c.Decimal(nullable: false, precision: 18, scale: 2),
                        PriceGross = c.Decimal(nullable: false, precision: 18, scale: 2),
                        Archived = c.Boolean(nullable: false),
                        CreatedDate = c.DateTime(nullable: false, precision: 7, storeType: "datetime2"),
                        UpdatedDate = c.DateTime(nullable: false, precision: 7, storeType: "datetime2"),
                    })
                .PrimaryKey(t => t.Id);
            
            CreateTable(
                "dbo.ProductCategories",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        CategoryId = c.Int(nullable: false),
                        ParentID = c.Int(nullable: false),
                        Name = c.String(),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.Components",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        TwrID = c.Int(nullable: false),
                        CodeGaska = c.String(),
                        Qty = c.Single(nullable: false),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.CrossNumbers",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        CrossNumberValue = c.String(),
                        CrossManufacturer = c.String(),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.ProductFiles",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        Title = c.String(),
                        Url = c.String(),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.ProductImages",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        Title = c.String(),
                        Url = c.String(),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.Packages",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        PackUnit = c.String(),
                        PackQty = c.Single(nullable: false),
                        PackNettWeight = c.Single(nullable: false),
                        PackGrossWeight = c.Single(nullable: false),
                        PackEan = c.String(),
                        PackRequired = c.Int(nullable: false),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.ProductParameters",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        AttributeId = c.Int(nullable: false),
                        AttributeName = c.String(),
                        AttributeValue = c.String(),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
            CreateTable(
                "dbo.RecommendedParts",
                c => new
                    {
                        Id = c.Int(nullable: false, identity: true),
                        TwrID = c.Int(nullable: false),
                        CodeGaska = c.String(),
                        Qty = c.Single(nullable: false),
                        ProductId = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Applications", "ProductId", "dbo.Products");
            DropForeignKey("dbo.RecommendedParts", "ProductId", "dbo.Products");
            DropForeignKey("dbo.ProductParameters", "ProductId", "dbo.Products");
            DropForeignKey("dbo.Packages", "ProductId", "dbo.Products");
            DropForeignKey("dbo.ProductImages", "ProductId", "dbo.Products");
            DropForeignKey("dbo.ProductFiles", "ProductId", "dbo.Products");
            DropForeignKey("dbo.CrossNumbers", "ProductId", "dbo.Products");
            DropForeignKey("dbo.Components", "ProductId", "dbo.Products");
            DropForeignKey("dbo.ProductCategories", "ProductId", "dbo.Products");
            DropIndex("dbo.RecommendedParts", new[] { "ProductId" });
            DropIndex("dbo.ProductParameters", new[] { "ProductId" });
            DropIndex("dbo.Packages", new[] { "ProductId" });
            DropIndex("dbo.ProductImages", new[] { "ProductId" });
            DropIndex("dbo.ProductFiles", new[] { "ProductId" });
            DropIndex("dbo.CrossNumbers", new[] { "ProductId" });
            DropIndex("dbo.Components", new[] { "ProductId" });
            DropIndex("dbo.ProductCategories", new[] { "ProductId" });
            DropIndex("dbo.Applications", new[] { "ProductId" });
            DropTable("dbo.RecommendedParts");
            DropTable("dbo.ProductParameters");
            DropTable("dbo.Packages");
            DropTable("dbo.ProductImages");
            DropTable("dbo.ProductFiles");
            DropTable("dbo.CrossNumbers");
            DropTable("dbo.Components");
            DropTable("dbo.ProductCategories");
            DropTable("dbo.Products");
            DropTable("dbo.Applications");
            DropTable("dbo.AllegroTokenEntities");
        }
    }
}
