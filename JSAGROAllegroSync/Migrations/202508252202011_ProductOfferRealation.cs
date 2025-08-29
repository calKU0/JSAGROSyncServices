namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class ProductOfferRealation : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOffers", "ProductId", c => c.Int());
            AlterColumn("dbo.AllegroOffers", "ExternalId", c => c.String());
            CreateIndex("dbo.AllegroOffers", "ProductId");
            AddForeignKey("dbo.AllegroOffers", "ProductId", "dbo.Products", "Id");
        }

        public override void Down()
        {
            DropForeignKey("dbo.AllegroOffers", "ProductId", "dbo.Products");
            DropIndex("dbo.AllegroOffers", new[] { "ProductId" });
            AlterColumn("dbo.AllegroOffers", "ExternalId", c => c.String(maxLength: 100));
            DropColumn("dbo.AllegroOffers", "ProductId");
        }
    }
}