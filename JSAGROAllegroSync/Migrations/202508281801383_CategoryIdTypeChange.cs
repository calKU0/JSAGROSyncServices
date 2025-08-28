namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CategoryIdTypeChange : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.AllegroCategories", "CategoryId", c => c.String());
        }
        
        public override void Down()
        {
            AlterColumn("dbo.AllegroCategories", "CategoryId", c => c.Int(nullable: false));
        }
    }
}
