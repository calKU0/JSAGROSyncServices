namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CategoryParametersfix : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.CategoryParameters", "ProductId", "dbo.Products");
            DropIndex("dbo.CategoryParameters", new[] { "ProductId" });
            DropPrimaryKey("dbo.CategoryParameters");
            AlterColumn("dbo.CategoryParameters", "ParameterId", c => c.Int(nullable: false));
            AddPrimaryKey("dbo.CategoryParameters", new[] { "ParameterId", "CategoryId" });
            DropColumn("dbo.CategoryParameters", "Id");
            DropColumn("dbo.CategoryParameters", "ProductId");
        }
        
        public override void Down()
        {
            AddColumn("dbo.CategoryParameters", "ProductId", c => c.Int(nullable: false));
            AddColumn("dbo.CategoryParameters", "Id", c => c.Int(nullable: false, identity: true));
            DropPrimaryKey("dbo.CategoryParameters");
            AlterColumn("dbo.CategoryParameters", "ParameterId", c => c.String());
            AddPrimaryKey("dbo.CategoryParameters", "Id");
            CreateIndex("dbo.CategoryParameters", "ProductId");
            AddForeignKey("dbo.CategoryParameters", "ProductId", "dbo.Products", "Id", cascadeDelete: true);
        }
    }
}
