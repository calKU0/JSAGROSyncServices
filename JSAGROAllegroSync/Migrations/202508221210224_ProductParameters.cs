namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class ProductParameters : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.ProductAttributes",
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

            AddColumn("dbo.ProductParameters", "ParameterId", c => c.Int(nullable: false));
            AddColumn("dbo.ProductParameters", "Value", c => c.String());
            AddColumn("dbo.ProductParameters", "CategoryParameter_ParameterId", c => c.Int());
            AddColumn("dbo.ProductParameters", "CategoryParameter_CategoryId", c => c.Int());
            CreateIndex("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" });
            AddForeignKey("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" }, "dbo.CategoryParameters", new[] { "ParameterId", "CategoryId" });
            DropColumn("dbo.ProductParameters", "AttributeId");
            DropColumn("dbo.ProductParameters", "AttributeName");
            DropColumn("dbo.ProductParameters", "AttributeValue");
        }

        public override void Down()
        {
            AddColumn("dbo.ProductParameters", "AttributeValue", c => c.String());
            AddColumn("dbo.ProductParameters", "AttributeName", c => c.String());
            AddColumn("dbo.ProductParameters", "AttributeId", c => c.Int(nullable: false));
            DropForeignKey("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" }, "dbo.CategoryParameters");
            DropForeignKey("dbo.ProductAttributes", "ProductId", "dbo.Products");
            DropIndex("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" });
            DropIndex("dbo.ProductAttributes", new[] { "ProductId" });
            DropColumn("dbo.ProductParameters", "CategoryParameter_CategoryId");
            DropColumn("dbo.ProductParameters", "CategoryParameter_ParameterId");
            DropColumn("dbo.ProductParameters", "Value");
            DropColumn("dbo.ProductParameters", "ParameterId");
            DropTable("dbo.ProductAttributes");
        }
    }
}