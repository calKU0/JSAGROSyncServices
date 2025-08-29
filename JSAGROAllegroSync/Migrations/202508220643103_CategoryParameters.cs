namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class CategoryParameters : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CategoryParameters",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    ProductId = c.Int(nullable: false),
                    CategoryId = c.Int(nullable: false),
                    ParameterId = c.String(),
                    Name = c.String(),
                    Type = c.String(),
                    Required = c.Boolean(nullable: false),
                    Min = c.Int(),
                    Max = c.Int(),
                })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.Products", t => t.ProductId, cascadeDelete: true)
                .Index(t => t.ProductId);
        }

        public override void Down()
        {
            DropForeignKey("dbo.CategoryParameters", "ProductId", "dbo.Products");
            DropIndex("dbo.CategoryParameters", new[] { "ProductId" });
            DropTable("dbo.CategoryParameters");
        }
    }
}