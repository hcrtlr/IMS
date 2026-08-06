using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Database-level guarantees that EF's fluent API cannot express.
///
/// 1. Doc §5.1 - the InventoryBalance uniqueness tuple contains nullable columns, and
///    the doc explicitly says PostgreSQL's NULL-distinct behaviour must be considered.
///    A plain unique index would let unlimited duplicate rows exist whenever lot,
///    serial and LPN are all null (the common case). The index is therefore built over
///    COALESCE(col, '000...0') so nulls collide like any other value.
///
/// 2. Doc §11.10 / §5.6 - InventoryTransaction rows are immutable. The DbContext already
///    rejects Modified/Deleted entries, but triggers make that hold for anything that
///    reaches the database by another route (psql, a future service, a bad migration).
///
/// 3. Rule §11.1 / acceptance scenario 3 - no path may drive a balance negative. Check
///    constraints make that a storage-level invariant rather than a convention.
/// </summary>
public partial class InventoryIntegrityConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // --- 1. NULL-safe unique index on the doc §5.1 tuple -------------------------
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX ux_inventory_balances_unique_combo
            ON inventory_balances (
                "WarehouseId",
                "LocationId",
                "ItemId",
                "InventoryStatusId",
                COALESCE("LotId",          '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE("SerialId",       '00000000-0000-0000-0000-000000000000'::uuid),
                COALESCE("LicensePlateId", '00000000-0000-0000-0000-000000000000'::uuid)
            );
            """);

        // --- 2. Non-negative stock invariants ---------------------------------------
        migrationBuilder.Sql("""
            ALTER TABLE inventory_balances
                ADD CONSTRAINT ck_inventory_balances_non_negative
                CHECK ("OnHandQuantity" >= 0
                       AND "AllocatedQuantity" >= 0
                       AND "HoldQuantity" >= 0);
            """);

        // Doc §5.1 formula: reserved + held can never exceed what is physically present.
        migrationBuilder.Sql("""
            ALTER TABLE inventory_balances
                ADD CONSTRAINT ck_inventory_balances_available_non_negative
                CHECK ("OnHandQuantity" - "AllocatedQuantity" - "HoldQuantity" >= 0);
            """);

        // --- 3. Immutable ledger (doc §11.10) ---------------------------------------
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION ims_prevent_transaction_mutation()
            RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION
                    'inventory_transactions is an immutable ledger (doc rule 11.10); % is not permitted',
                    TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;
            """);

        migrationBuilder.Sql("""
            CREATE TRIGGER trg_inventory_transactions_no_update
            BEFORE UPDATE ON inventory_transactions
            FOR EACH ROW EXECUTE FUNCTION ims_prevent_transaction_mutation();
            """);

        migrationBuilder.Sql("""
            CREATE TRIGGER trg_inventory_transactions_no_delete
            BEFORE DELETE ON inventory_transactions
            FOR EACH ROW EXECUTE FUNCTION ims_prevent_transaction_mutation();
            """);

        // --- 4. Rule §11.5 - serial-tracked stock never exceeds one unit -------------
        migrationBuilder.Sql("""
            ALTER TABLE inventory_balances
                ADD CONSTRAINT ck_inventory_balances_serial_single_unit
                CHECK ("SerialId" IS NULL OR "OnHandQuantity" <= 1);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_inventory_transactions_no_delete ON inventory_transactions;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_inventory_transactions_no_update ON inventory_transactions;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS ims_prevent_transaction_mutation();");

        migrationBuilder.Sql("ALTER TABLE inventory_balances DROP CONSTRAINT IF EXISTS ck_inventory_balances_serial_single_unit;");
        migrationBuilder.Sql("ALTER TABLE inventory_balances DROP CONSTRAINT IF EXISTS ck_inventory_balances_available_non_negative;");
        migrationBuilder.Sql("ALTER TABLE inventory_balances DROP CONSTRAINT IF EXISTS ck_inventory_balances_non_negative;");

        migrationBuilder.Sql("DROP INDEX IF EXISTS ux_inventory_balances_unique_combo;");
    }
}
