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
    [Migration("20260906000000_AddCreatedRequisitions")]
    public partial class AddCreatedRequisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "created");

            migrationBuilder.CreateTable(
                name: "created_requisitions",
                schema: "created",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    item = table.Column<string>(type: "character varying(28)", maxLength: 28, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    date = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requester = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_created_requisitions", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "created_requisitions",
                schema: "created");
        }
    }
}