namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AllegroCategories : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.AllegroCategories",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    CategoryId = c.Int(nullable: false),
                    Name = c.String(),
                    ParentCategoryId = c.String(),
                    Parent_Id = c.Int(),
                })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.AllegroCategories", t => t.Parent_Id)
                .Index(t => t.Parent_Id);
        }

        public override void Down()
        {
            DropForeignKey("dbo.AllegroCategories", "Parent_Id", "dbo.AllegroCategories");
            DropIndex("dbo.AllegroCategories", new[] { "Parent_Id" });
            DropTable("dbo.AllegroCategories");
        }
    }
}