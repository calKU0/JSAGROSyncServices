namespace GaskaAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class ParametersFields : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ProductParameters", "IsForProduct", c => c.Boolean(nullable: false));
            AddColumn("dbo.CategoryParameters", "RequiredForProduct", c => c.Boolean(nullable: false));
            AddColumn("dbo.CategoryParameters", "DescribesProduct", c => c.Boolean(nullable: false));
            AddColumn("dbo.CategoryParameters", "CustomValuesEnabled", c => c.Boolean(nullable: false));
            AddColumn("dbo.CategoryParameters", "AmbiguousValueId", c => c.String());
        }

        public override void Down()
        {
            DropColumn("dbo.CategoryParameters", "AmbiguousValueId");
            DropColumn("dbo.CategoryParameters", "CustomValuesEnabled");
            DropColumn("dbo.CategoryParameters", "DescribesProduct");
            DropColumn("dbo.CategoryParameters", "RequiredForProduct");
            DropColumn("dbo.ProductParameters", "IsForProduct");
        }
    }
}