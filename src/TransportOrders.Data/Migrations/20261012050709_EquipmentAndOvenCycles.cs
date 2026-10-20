using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportOrders.Data.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentAndOvenCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OvenRecipes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Alloy = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    TargetTemperature = table.Column<decimal>(type: "TEXT", nullable: false),
                    HeatingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    HoldingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CoolingMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvenRecipes", x => x.Id);
                    table.CheckConstraint("CK_OvenRecipes_Durations", "HeatingMinutes > 0 AND HoldingMinutes > 0 AND CoolingMinutes > 0");
                });

            migrationBuilder.CreateTable(
                name: "PlantEquipment",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LocationId = table.Column<string>(type: "TEXT", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Condition = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ActiveOperation = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantEquipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlantEquipment_PlantLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    By = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    EquipmentId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentEvents_PlantEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "PlantEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OvenCycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquipmentId = table.Column<string>(type: "TEXT", nullable: false),
                    RecipeId = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTemperature = table.Column<decimal>(type: "TEXT", nullable: false),
                    HeatingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    HoldingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CoolingMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ElapsedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvenCycles", x => x.Id);
                    table.CheckConstraint("CK_OvenCycles_Elapsed", "ElapsedMinutes >= 0 AND ElapsedMinutes <= HeatingMinutes + HoldingMinutes + CoolingMinutes");
                    table.ForeignKey(
                        name: "FK_OvenCycles_OvenRecipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "OvenRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvenCycles_PlantEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "PlantEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OvenCycleLoads",
                columns: table => new
                {
                    CycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BasketNumber = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialLotNumber = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvenCycleLoads", x => new { x.CycleId, x.BasketNumber });
                    table.ForeignKey(
                        name: "FK_OvenCycleLoads_Baskets_BasketNumber",
                        column: x => x.BasketNumber,
                        principalTable: "Baskets",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvenCycleLoads_MaterialLots_MaterialLotNumber",
                        column: x => x.MaterialLotNumber,
                        principalTable: "MaterialLots",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvenCycleLoads_OvenCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "OvenCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentEvents_EquipmentId",
                table: "EquipmentEvents",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OvenCycleLoads_BasketNumber",
                table: "OvenCycleLoads",
                column: "BasketNumber");

            migrationBuilder.CreateIndex(
                name: "IX_OvenCycleLoads_MaterialLotNumber",
                table: "OvenCycleLoads",
                column: "MaterialLotNumber");

            migrationBuilder.CreateIndex(
                name: "IX_OvenCycles_EquipmentId",
                table: "OvenCycles",
                column: "EquipmentId",
                unique: true,
                filter: "Phase <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "IX_OvenCycles_RecipeId",
                table: "OvenCycles",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantEquipment_LocationId",
                table: "PlantEquipment",
                column: "LocationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentEvents");

            migrationBuilder.DropTable(
                name: "OvenCycleLoads");

            migrationBuilder.DropTable(
                name: "OvenCycles");

            migrationBuilder.DropTable(
                name: "OvenRecipes");

            migrationBuilder.DropTable(
                name: "PlantEquipment");
        }
    }
}
