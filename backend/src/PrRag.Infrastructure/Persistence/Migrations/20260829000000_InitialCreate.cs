using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PrRag.Infrastructure.Persistence;

#nullable disable

namespace PrRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(PrRagDbContext))]
    [Migration("20260829000000_InitialCreate")]
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "purchase_requisitions",
                columns: table => new
                {
                    purchase_requisition = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    item = table.Column<string>(type: "character varying(28)", maxLength: 28, nullable: false),
                    item_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    embedding = table.Column<float[]>(type: "vector(1536)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_requisitions", x => x.purchase_requisition);
                });

            migrationBuilder.Sql(
                "CREATE INDEX IX_purchase_requisitions_embedding ON purchase_requisitions USING hnsw (embedding vector_cosine_ops);");

            migrationBuilder.CreateTable(
                name: "data_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_sync = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_status", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "data_status");
            migrationBuilder.DropTable(name: "purchase_requisitions");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS vector;");
        }
    }
}
