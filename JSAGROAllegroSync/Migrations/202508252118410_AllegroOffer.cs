namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AllegroOffer : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.AllegroOffers",
                c => new
                    {
                        Id = c.String(nullable: false, maxLength: 128),
                        ExternalId = c.String(),
                        Name = c.String(),
                        CategoryId = c.Int(nullable: false),
                        Price = c.Decimal(nullable: false, precision: 18, scale: 2),
                        Stock = c.Int(nullable: false),
                        WatchersCount = c.Int(nullable: false),
                        VisitsCount = c.Int(nullable: false),
                        Status = c.String(),
                        DeliveryName = c.String(),
                    })
                .PrimaryKey(t => t.Id);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.AllegroOffers");
        }
    }
}
