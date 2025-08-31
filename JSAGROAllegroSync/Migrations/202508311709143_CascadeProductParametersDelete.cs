namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CascadeProductParametersDelete : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.ProductParameters", "CategoryParameterId", "dbo.CategoryParameters");
            AddColumn("dbo.ProductParameters", "CategoryParameter_Id", c => c.Int());
            CreateIndex("dbo.ProductParameters", "CategoryParameter_Id");
            AddForeignKey("dbo.ProductParameters", "CategoryParameterId", "dbo.CategoryParameters", "Id");
            AddForeignKey("dbo.ProductParameters", "CategoryParameter_Id", "dbo.CategoryParameters", "Id");
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.ProductParameters", "CategoryParameter_Id", "dbo.CategoryParameters");
            DropForeignKey("dbo.ProductParameters", "CategoryParameterId", "dbo.CategoryParameters");
            DropIndex("dbo.ProductParameters", new[] { "CategoryParameter_Id" });
            DropColumn("dbo.ProductParameters", "CategoryParameter_Id");
            AddForeignKey("dbo.ProductParameters", "CategoryParameterId", "dbo.CategoryParameters", "Id", cascadeDelete: true);
        }
    }
}
