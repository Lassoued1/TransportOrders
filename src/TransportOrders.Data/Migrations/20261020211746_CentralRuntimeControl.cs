using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportOrders.Data.Migrations
{
    /// <inheritdoc />
    public partial class CentralRuntimeControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuntimeControl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeControl", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeControlEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Paused = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeControlEvents", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RuntimeControl",
                columns: new[] { "Id", "Paused", "Version" },
                values: new object[] { 1, false, new Guid("a721ab3c-5384-48c6-b00e-a6db4a859d2e") });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeControlEvents_AtUtc",
                table: "RuntimeControlEvents",
                column: "AtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeControl");

            migrationBuilder.DropTable(
                name: "RuntimeControlEvents");
        }
    }
}
