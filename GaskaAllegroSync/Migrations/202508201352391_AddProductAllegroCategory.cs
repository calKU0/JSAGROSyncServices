namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AddProductAllegroCategory : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Products", "DefaultAllegroCategory", c => c.Int(nullable: false));
        }

        public override void Down()
        {
            DropColumn("dbo.Products", "DefaultAllegroCategory");
        }
    }
}