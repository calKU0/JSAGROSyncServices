namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AddErliBool : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOffers", "ExistsInErli", c => c.Boolean(nullable: false));
        }

        public override void Down()
        {
            DropColumn("dbo.AllegroOffers", "ExistsInErli");
        }
    }
}