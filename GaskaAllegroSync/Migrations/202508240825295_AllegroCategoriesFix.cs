namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class AllegroCategoriesFix : DbMigration
    {
        public override void Up()
        {
            RenameColumn(table: "dbo.AllegroCategories", name: "Parent_Id", newName: "ParentId");
            RenameIndex(table: "dbo.AllegroCategories", name: "IX_Parent_Id", newName: "IX_ParentId");
            DropColumn("dbo.AllegroCategories", "ParentCategoryId");
        }

        public override void Down()
        {
            AddColumn("dbo.AllegroCategories", "ParentCategoryId", c => c.String());
            RenameIndex(table: "dbo.AllegroCategories", name: "IX_ParentId", newName: "IX_Parent_Id");
            RenameColumn(table: "dbo.AllegroCategories", name: "ParentId", newName: "Parent_Id");
        }
    }
}