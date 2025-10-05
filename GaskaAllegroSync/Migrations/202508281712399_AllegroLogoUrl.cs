namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AllegroLogoUrl : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ProductImages", "AllegroLogoUrl", c => c.String());
        }

        public override void Down()
        {
            DropColumn("dbo.ProductImages", "AllegroLogoUrl");
        }
    }
}