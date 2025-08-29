namespace JSAGROAllegroSync.Migrations
{
    using System.Data.Entity.Migrations;

    public partial class ParametersLinkFix : DbMigration
    {
        public override void Up()
        {
            DropPrimaryKey("dbo.CategoryParameters");
            CreateTable(
                "dbo.CategoryParameterValues",
                c => new
                {
                    Id = c.Int(nullable: false, identity: true),
                    CategoryParameterId = c.Int(nullable: false),
                    Value = c.String(),
                })
                .PrimaryKey(t => t.Id)
                .ForeignKey("dbo.CategoryParameters", t => t.CategoryParameterId, cascadeDelete: true)
                .Index(t => t.CategoryParameterId);

            AddColumn("dbo.ProductParameters", "CategoryParameterId", c => c.Int(nullable: false));
            AddColumn("dbo.CategoryParameters", "Id", c => c.Int(nullable: false, identity: true));
            AddPrimaryKey("dbo.CategoryParameters", "Id");
            CreateIndex("dbo.ProductParameters", "CategoryParameterId");
            CreateIndex("dbo.CategoryParameters", new[] { "ParameterId", "CategoryId" }, unique: true);
            AddForeignKey("dbo.ProductParameters", "CategoryParameterId", "dbo.CategoryParameters", "Id", cascadeDelete: true);
            DropColumn("dbo.ProductParameters", "ParameterId");
        }

        public override void Down()
        {
            AddColumn("dbo.ProductParameters", "ParameterId", c => c.Int(nullable: false));
            DropForeignKey("dbo.CategoryParameterValues", "CategoryParameterId", "dbo.CategoryParameters");
            DropForeignKey("dbo.ProductParameters", "CategoryParameterId", "dbo.CategoryParameters");
            DropIndex("dbo.CategoryParameterValues", new[] { "CategoryParameterId" });
            DropIndex("dbo.CategoryParameters", new[] { "ParameterId", "CategoryId" });
            DropIndex("dbo.ProductParameters", new[] { "CategoryParameterId" });
            DropPrimaryKey("dbo.CategoryParameters");
            DropColumn("dbo.CategoryParameters", "Id");
            DropColumn("dbo.ProductParameters", "CategoryParameterId");
            DropTable("dbo.CategoryParameterValues");
            AddPrimaryKey("dbo.CategoryParameters", new[] { "ParameterId", "CategoryId" });
        }
    }
}