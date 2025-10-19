namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class HandlingTime : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOffers", "HandlingTime", c => c.String());
        }

        public override void Down()
        {
            DropColumn("dbo.AllegroOffers", "HandlingTime");
        }
    }
}