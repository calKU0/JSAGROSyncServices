namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class OffertStartingAt : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOffers", "StartingAt", c => c.DateTime(nullable: false));
        }

        public override void Down()
        {
            DropColumn("dbo.AllegroOffers", "StartingAt");
        }
    }
}