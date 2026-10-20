using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportOrders.Data.Migrations
{
    /// <inheritdoc />
    public partial class PackingAndReusableBaskets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Baskets_Position",
                table: "Baskets");

            migrationBuilder.AddColumn<int>(
                name: "BasketContentSequence",
                table: "TransportOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ContentSequence",
                table: "OvenCycleLoads",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveReturnId",
                table: "Baskets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentSequence",
                table: "Baskets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "BasketEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    User = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BasketNumber = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BasketEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BasketEvents_Baskets_BasketNumber",
                        column: x => x.BasketNumber,
                        principalTable: "Baskets",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BasketReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BasketNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceLocation = table.Column<string>(type: "TEXT", nullable: false),
                    TargetLocation = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedBy = table.Column<string>(type: "TEXT", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArrivedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BasketReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BasketReturns_Baskets_BasketNumber",
                        column: x => x.BasketNumber,
                        principalTable: "Baskets",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BasketReturns_PlantLocations_SourceLocation",
                        column: x => x.SourceLocation,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BasketReturns_PlantLocations_TargetLocation",
                        column: x => x.TargetLocation,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackedPackages",
                columns: table => new
                {
                    Number = table.Column<string>(type: "TEXT", nullable: false),
                    SourceBasketNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ContentSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialLotNumber = table.Column<string>(type: "TEXT", nullable: false),
                    BatchNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Material = table.Column<string>(type: "TEXT", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<decimal>(type: "TEXT", nullable: false),
                    LoadingLength = table.Column<decimal>(type: "TEXT", nullable: false),
                    EquipmentId = table.Column<string>(type: "TEXT", nullable: false),
                    LocationId = table.Column<string>(type: "TEXT", nullable: true),
                    ReservedLocationId = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    VisualPassed = table.Column<bool>(type: "INTEGER", nullable: true),
                    QuantityVerified = table.Column<bool>(type: "INTEGER", nullable: true),
                    LabelText = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ShippedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackedPackages", x => x.Number);
                    table.ForeignKey(
                        name: "FK_PackedPackages_Baskets_SourceBasketNumber",
                        column: x => x.SourceBasketNumber,
                        principalTable: "Baskets",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackedPackages_MaterialLots_MaterialLotNumber",
                        column: x => x.MaterialLotNumber,
                        principalTable: "MaterialLots",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackedPackages_PlantEquipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "PlantEquipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackedPackages_PlantLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackedPackages_PlantLocations_ReservedLocationId",
                        column: x => x.ReservedLocationId,
                        principalTable: "PlantLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackageEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    User = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PackageNumber = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageEvents_PackedPackages_PackageNumber",
                        column: x => x.PackageNumber,
                        principalTable: "PackedPackages",
                        principalColumn: "Number",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Baskets_ActiveReturnId",
                table: "Baskets",
                column: "ActiveReturnId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Baskets_Position",
                table: "Baskets",
                sql: "(LocationId IS NOT NULL AND ReservedLocationId IS NULL AND ActiveTransportNumber IS NULL AND ActiveReturnId IS NULL) OR (LocationId IS NULL AND ReservedLocationId IS NOT NULL AND ((ActiveTransportNumber IS NOT NULL AND ActiveReturnId IS NULL) OR (ActiveTransportNumber IS NULL AND ActiveReturnId IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_BasketEvents_BasketNumber",
                table: "BasketEvents",
                column: "BasketNumber");

            migrationBuilder.CreateIndex(
                name: "IX_BasketReturns_BasketNumber",
                table: "BasketReturns",
                column: "BasketNumber",
                unique: true,
                filter: "ArrivedAt IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BasketReturns_SourceLocation",
                table: "BasketReturns",
                column: "SourceLocation");

            migrationBuilder.CreateIndex(
                name: "IX_BasketReturns_TargetLocation",
                table: "BasketReturns",
                column: "TargetLocation");

            migrationBuilder.CreateIndex(
                name: "IX_PackageEvents_PackageNumber",
                table: "PackageEvents",
                column: "PackageNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PackedPackages_EquipmentId",
                table: "PackedPackages",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PackedPackages_LocationId",
                table: "PackedPackages",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PackedPackages_MaterialLotNumber",
                table: "PackedPackages",
                column: "MaterialLotNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PackedPackages_ReservedLocationId",
                table: "PackedPackages",
                column: "ReservedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PackedPackages_SourceBasketNumber_ContentSequence",
                table: "PackedPackages",
                columns: new[] { "SourceBasketNumber", "ContentSequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Baskets_BasketReturns_ActiveReturnId",
                table: "Baskets",
                column: "ActiveReturnId",
                principalTable: "BasketReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Baskets_BasketReturns_ActiveReturnId",
                table: "Baskets");

            migrationBuilder.DropTable(
                name: "BasketEvents");

            migrationBuilder.DropTable(
                name: "BasketReturns");

            migrationBuilder.DropTable(
                name: "PackageEvents");

            migrationBuilder.DropTable(
                name: "PackedPackages");

            migrationBuilder.DropIndex(
                name: "IX_Baskets_ActiveReturnId",
                table: "Baskets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Baskets_Position",
                table: "Baskets");

            migrationBuilder.DropColumn(
                name: "BasketContentSequence",
                table: "TransportOrders");

            migrationBuilder.DropColumn(
                name: "ContentSequence",
                table: "OvenCycleLoads");

            migrationBuilder.DropColumn(
                name: "ActiveReturnId",
                table: "Baskets");

            migrationBuilder.DropColumn(
                name: "ContentSequence",
                table: "Baskets");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Baskets_Position",
                table: "Baskets",
                sql: "(LocationId IS NOT NULL AND ReservedLocationId IS NULL AND ActiveTransportNumber IS NULL) OR (LocationId IS NULL AND ReservedLocationId IS NOT NULL AND ActiveTransportNumber IS NOT NULL)");
        }
    }
}
