using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportOrders.Data.Migrations
{
    /// <inheritdoc />
    public partial class VersionedOvenRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "OvenRecipes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "OvenRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: "Validated");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "OvenRecipes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "RecipeRevision",
                table: "OvenCycles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "RecipeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Alloy = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTemperature = table.Column<decimal>(type: "TEXT", nullable: false),
                    HeatingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    HoldingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CoolingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeEvents_OvenRecipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "OvenRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeEvents_RecipeId",
                table: "RecipeEvents",
                column: "RecipeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeEvents");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "OvenRecipes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OvenRecipes");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "OvenRecipes");

            migrationBuilder.DropColumn(
                name: "RecipeRevision",
                table: "OvenCycles");
        }
    }
}
