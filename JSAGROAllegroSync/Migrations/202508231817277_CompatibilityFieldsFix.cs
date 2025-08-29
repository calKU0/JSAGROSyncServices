namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class CompatibilityFieldsFix : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.CompatibleProducts", "Name", c => c.String());
            AddColumn("dbo.CompatibleProducts", "GroupName", c => c.String());
            DropColumn("dbo.CompatibleProducts", "Text");
            DropColumn("dbo.CompatibleProducts", "GroupId");
        }

        public override void Down()
        {
            AddColumn("dbo.CompatibleProducts", "GroupId", c => c.String());
            AddColumn("dbo.CompatibleProducts", "Text", c => c.String());
            DropColumn("dbo.CompatibleProducts", "GroupName");
            DropColumn("dbo.CompatibleProducts", "Name");
        }
    }
}