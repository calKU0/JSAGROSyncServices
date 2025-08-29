namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class ProductParametersfix : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" }, "dbo.CategoryParameters");
            DropIndex("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" });
            DropColumn("dbo.ProductParameters", "CategoryParameter_ParameterId");
            DropColumn("dbo.ProductParameters", "CategoryParameter_CategoryId");
        }

        public override void Down()
        {
            AddColumn("dbo.ProductParameters", "CategoryParameter_CategoryId", c => c.Int());
            AddColumn("dbo.ProductParameters", "CategoryParameter_ParameterId", c => c.Int());
            CreateIndex("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" });
            AddForeignKey("dbo.ProductParameters", new[] { "CategoryParameter_ParameterId", "CategoryParameter_CategoryId" }, "dbo.CategoryParameters", new[] { "ParameterId", "CategoryId" });
        }
    }
}