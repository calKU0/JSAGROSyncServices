namespace GaskaAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddMoreFields : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOffers", "Images", c => c.String());
            AddColumn("dbo.AllegroOffers", "Weight", c => c.Decimal(nullable: false, precision: 18, scale: 2));
        }
        
        public override void Down()
        {
            DropColumn("dbo.AllegroOffers", "Weight");
            DropColumn("dbo.AllegroOffers", "Images");
        }
    }
}
