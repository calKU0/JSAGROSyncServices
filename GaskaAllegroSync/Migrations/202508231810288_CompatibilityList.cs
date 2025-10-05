namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class CompatibilityList : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.CompatibleProducts",
                c => new
                {
                    Id = c.String(nullable: false, maxLength: 128),
                    Text = c.String(),
                    Type = c.String(),
                    GroupId = c.String(),
                })
                .PrimaryKey(t => t.Id);
        }

        public override void Down()
        {
            DropTable("dbo.CompatibleProducts");
        }
    }
}