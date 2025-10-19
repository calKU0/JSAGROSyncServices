namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AddOffersDetails : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.AllegroOfferAttributes",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    OfferId = c.String(maxLength: 128),
                    AttributeId = c.String(),
                    Type = c.String(),
                    ValuesJson = c.String(),
                    ValuesIdsJson = c.String(),
                })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.AllegroOffers", t => t.OfferId)
                .Index(t => t.OfferId);

            CreateTable(
                "dbo.AllegroOfferDescriptions",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    OfferId = c.String(maxLength: 128),
                    Type = c.String(),
                    Content = c.String(),
                })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.AllegroOffers", t => t.OfferId)
                .Index(t => t.OfferId);
        }

        public override void Down()
        {
            DropForeignKey("dbo.AllegroOfferDescriptions", "OfferId", "dbo.AllegroOffers");
            DropForeignKey("dbo.AllegroOfferAttributes", "OfferId", "dbo.AllegroOffers");
            DropIndex("dbo.AllegroOfferDescriptions", new[] { "OfferId" });
            DropIndex("dbo.AllegroOfferAttributes", new[] { "OfferId" });
            DropTable("dbo.AllegroOfferDescriptions");
            DropTable("dbo.AllegroOfferAttributes");
        }
    }
}