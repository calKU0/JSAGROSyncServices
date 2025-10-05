namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AllegroExpirationDateFix : DbMigration
    {
        public override void Up()
        {
            AlterColumn("dbo.ProductImages", "AllegroExpirationDate", c => c.DateTime(nullable: false, precision: 7, storeType: "datetime2"));
        }

        public override void Down()
        {
            AlterColumn("dbo.ProductImages", "AllegroExpirationDate", c => c.DateTime(nullable: false));
        }
    }
}