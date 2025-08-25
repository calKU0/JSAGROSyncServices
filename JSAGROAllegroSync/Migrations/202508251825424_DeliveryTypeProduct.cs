namespace JSAGROAllegroSync.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class DeliveryTypeProduct : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Products", "DeliveryType", c => c.Int(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.Products", "DeliveryType");
        }
    }
}
