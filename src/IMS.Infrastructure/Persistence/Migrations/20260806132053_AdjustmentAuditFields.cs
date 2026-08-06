using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdjustmentAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DriftedBeforeApproval",
                table: "inventory_adjustments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryTransactionId",
                table: "inventory_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityAfterApproval",
                table: "inventory_adjustments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityBeforeApproval",
                table: "inventory_adjustments",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriftedBeforeApproval",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "InventoryTransactionId",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "QuantityAfterApproval",
                table: "inventory_adjustments");

            migrationBuilder.DropColumn(
                name: "QuantityBeforeApproval",
                table: "inventory_adjustments");
        }
    }
}
