using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportOrders.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistedPlantModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaterialLots",
                columns: table => new
                {
                    Number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Material = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLots", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "PlantLocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantLocations", x => x.Id);
                    table.CheckConstraint("CK_PlantLocations_Capacity", "Capacity > 0");
                });

            migrationBuilder.CreateTable(
                name: "Baskets",
                columns: table => new
                {
                    Number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MaterialLotNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", nullable: false),
                    LoadingLength = table.Column<decimal>(type: "TEXT", nullable: false),
                    LocationId = table.Column<string>(type: "TEXT", nullable: true),
                    ReservedLocationId = table.Column<string>(type: "TEXT", nullable: true),
                    ActiveTransportNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Baskets", x => x.Number);
                    table.CheckConstraint("CK_Baskets_Position", "(LocationId IS NOT NULL AND ReservedLocationId IS NULL AND ActiveTransportNumber IS NULL) OR (LocationId IS NULL AND ReservedLocationId IS NOT NULL AND ActiveTransportNumber IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Baskets_MaterialLots_MaterialLotNumber",
                        column: x => x.MaterialLotNumber,
                        principalTable: "MaterialLots",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Baskets_PlantLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Baskets_PlantLocations_ReservedLocationId",
                        column: x => x.ReservedLocationId,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Baskets_Transports_ActiveTransportNumber",
                        column: x => x.ActiveTransportNumber,
                        principalTable: "Transports",
                        principalColumn: "TransportNumber",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Baskets_ActiveTransportNumber",
                table: "Baskets",
                column: "ActiveTransportNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Baskets_LocationId",
                table: "Baskets",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Baskets_MaterialLotNumber",
                table: "Baskets",
                column: "MaterialLotNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Baskets_ReservedLocationId",
                table: "Baskets",
                column: "ReservedLocationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Baskets");

            migrationBuilder.DropTable(
                name: "MaterialLots");

            migrationBuilder.DropTable(
                name: "PlantLocations");
        }
    }
}
