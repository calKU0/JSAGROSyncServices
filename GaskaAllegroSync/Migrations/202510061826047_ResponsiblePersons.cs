namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class ResponsiblePersons : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.AllegroOffers", "ResponsibleProducer", c => c.String());
            AddColumn("dbo.AllegroOffers", "ResponsiblePerson", c => c.String());
        }

        public override void Down()
        {
            DropColumn("dbo.AllegroOffers", "ResponsiblePerson");
            DropColumn("dbo.AllegroOffers", "ResponsibleProducer");
        }
    }
}