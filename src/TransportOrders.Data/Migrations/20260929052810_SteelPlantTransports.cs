using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportOrders.Data.Migrations
{
    /// <inheritdoc />
    public partial class SteelPlantTransports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Version 2 stored aluminium demo orders without pieces, weights or loading lengths; they cannot be
            // converted meaningfully. Clear them so the initializer seeds the steel-plant demo data instead.
            migrationBuilder.Sql("DELETE FROM StatusChanges;");
            migrationBuilder.Sql("DELETE FROM TransportOrders;");

            migrationBuilder.AddColumn<decimal>(
                name: "LoadingLength",
                table: "TransportOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ParentPieceNumber",
                table: "TransportOrders",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PieceNumber",
                table: "TransportOrders",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TransportNumber",
                table: "TransportOrders",
                type: "TEXT",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Weight",
                table: "TransportOrders",
                type: "TEXT",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "StatusChanges",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Transports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TransportNumber = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    Vehicle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LoadingLength = table.Column<decimal>(type: "TEXT", nullable: false),
                    Payload = table.Column<decimal>(type: "TEXT", nullable: false),
                    LoadedLength = table.Column<decimal>(type: "TEXT", nullable: false),
                    LoadedWeight = table.Column<decimal>(type: "TEXT", nullable: false),
                    OrderCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceLocation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetLocation = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PartLoadReason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DispatchedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transports", x => x.Id);
                    table.UniqueConstraint("AK_Transports_TransportNumber", x => x.TransportNumber);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransportOrders_TransportNumber",
                table: "TransportOrders",
                column: "TransportNumber");

            migrationBuilder.AddForeignKey(
                name: "FK_TransportOrders_Transports_TransportNumber",
                table: "TransportOrders",
                column: "TransportNumber",
                principalTable: "Transports",
                principalColumn: "TransportNumber",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransportOrders_Transports_TransportNumber",
                table: "TransportOrders");

            migrationBuilder.DropTable(
                name: "Transports");

            migrationBuilder.DropIndex(
                name: "IX_TransportOrders_TransportNumber",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "LoadingLength",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "ParentPieceNumber",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "PieceNumber",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "TransportNumber",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "StatusChanges");
        }
    }
}
