namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ProductAllegoImages : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ProductImages", "AllegroUrl", c => c.String());
            AddColumn("dbo.ProductImages", "AllegroExpirationDate", c => c.DateTime(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.ProductImages", "AllegroExpirationDate");
            DropColumn("dbo.ProductImages", "AllegroUrl");
        }
    }
}
