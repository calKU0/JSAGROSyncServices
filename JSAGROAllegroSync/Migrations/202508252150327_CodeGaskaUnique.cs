namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class CodeGaskaUnique : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.AllegroOffers", "ExternalId", c => c.String(maxLength: 100));
            AlterColumn("dbo.Products", "CodeGaska", c => c.String(maxLength: 100));
            CreateIndex("dbo.Products", "CodeGaska", unique: true);
        }
        
        public override void Down()
        {
            DropIndex("dbo.Products", new[] { "CodeGaska" });
            AlterColumn("dbo.Products", "CodeGaska", c => c.String());
            AlterColumn("dbo.AllegroOffers", "ExternalId", c => c.String());
        }
    }
}
