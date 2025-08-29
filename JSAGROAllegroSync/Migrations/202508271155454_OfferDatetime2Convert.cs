namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class OfferDatetime2Convert : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.AllegroOffers", "StartingAt", c => c.DateTime(nullable: false, precision: 7, storeType: "datetime2"));
        }

        public override void Down()
        {
            AlterColumn("dbo.AllegroOffers", "StartingAt", c => c.DateTime(nullable: false));
        }
    }
}