namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class SecionId : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOfferDescriptions", "SectionId", c => c.Int(nullable: false));
        }

        public override void Down()
        {
            DropColumn("dbo.AllegroOfferDescriptions", "SectionId");
        }
    }
}