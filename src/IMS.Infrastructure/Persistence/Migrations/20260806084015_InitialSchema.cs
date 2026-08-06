using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "algorithm_configurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlgorithmName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParameterKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParameterValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_algorithm_configurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "attribute_definitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsFilterable = table.Column<bool>(type: "boolean", nullable: false),
                    IsSlottingRelevant = table.Column<bool>(type: "boolean", nullable: false),
                    IsPickingRelevant = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attribute_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ShippingAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_statuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsAllocatable = table.Column<bool>(type: "boolean", nullable: false),
                    IsPhysicalStock = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_statuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "item_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_categories_item_categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "item_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "picking_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AlgorithmName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AlgorithmVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BatchingStrategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TotalStops = table.Column<int>(type: "integer", nullable: false),
                    EstimatedTravelDistance = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    ActualTravelDistance = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    EstimatedDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    ActualDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    AssignedTo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_picking_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "units_of_measure",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_units_of_measure", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    DefaultWarehouseId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SecurityStamp = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_users_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_warehouses_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "location_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LocationType = table.Column<int>(type: "integer", nullable: false),
                    MaxWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    MaxVolume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    AllowedItemCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    TemperatureMin = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    TemperatureMax = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    IsMixedItemAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsMixedLotAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_location_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_location_profiles_item_categories_AllowedItemCategoryId",
                        column: x => x.AllowedItemCategoryId,
                        principalTable: "item_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "inbound_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedArrivalDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inbound_orders_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inbound_orders_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequiredShipDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceLevel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    OrderType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TotalWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    TotalVolume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    TotalLineCount = table.Column<int>(type: "integer", nullable: false),
                    TotalQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_orders_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ZoneType = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_zones_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceLevel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ShippedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    TotalWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    TotalVolume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipments_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    BaseUomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Length = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Width = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Height = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Volume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    IsLotTracked = table.Column<bool>(type: "boolean", nullable: false),
                    IsSerialTracked = table.Column<bool>(type: "boolean", nullable: false),
                    IsExpirationTracked = table.Column<bool>(type: "boolean", nullable: false),
                    ShelfLifeDays = table.Column<int>(type: "integer", nullable: true),
                    IsFragile = table.Column<bool>(type: "boolean", nullable: false),
                    IsHazardous = table.Column<bool>(type: "boolean", nullable: false),
                    IsTemperatureControlled = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumStorageTemperature = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    MaximumStorageTemperature = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    StackableQuantity = table.Column<int>(type: "integer", nullable: true),
                    DefaultPutawayZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    DefaultPickZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_items_accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_item_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "item_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_items_units_of_measure_BaseUomId",
                        column: x => x.BaseUomId,
                        principalTable: "units_of_measure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_items_zones_DefaultPickZoneId",
                        column: x => x.DefaultPickZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_items_zones_DefaultPutawayZoneId",
                        column: x => x.DefaultPutawayZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Aisle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Bay = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Position = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LocationType = table.Column<int>(type: "integer", nullable: false),
                    LocationProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    PickSequence = table.Column<int>(type: "integer", nullable: true),
                    PutawaySequence = table.Column<int>(type: "integer", nullable: true),
                    CoordinateX = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    CoordinateY = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    CoordinateZ = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    MaxWeight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    MaxVolume = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    IsPickable = table.Column<bool>(type: "boolean", nullable: false),
                    IsPutawayAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DistanceToReceiving = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    DistanceToPacking = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    DistanceToShipping = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    AccessibilityScore = table.Column<int>(type: "integer", nullable: true),
                    MaxConcurrentWorkers = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_locations_location_profiles_LocationProfileId",
                        column: x => x.LocationProfileId,
                        principalTable: "location_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_locations_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_locations_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "count_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CountType = table.Column<int>(type: "integer", nullable: false),
                    SelectionMode = table.Column<int>(type: "integer", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BlockAllocationDuringCount = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_count_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_count_plans_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_plans_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_plans_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbound_order_details",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InboundOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpectedLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpectedExpirationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_order_details", x => x.Id);
                    table.CheckConstraint("ck_inbound_order_details_expected_positive", "\"ExpectedQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_inbound_order_details_inbound_orders_InboundOrderId",
                        column: x => x.InboundOrderId,
                        principalTable: "inbound_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inbound_order_details_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inbound_order_details_units_of_measure_UomId",
                        column: x => x.UomId,
                        principalTable: "units_of_measure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_attribute_values",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttributeDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TextValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NumberValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    BooleanValue = table.Column<bool>(type: "boolean", nullable: true),
                    DateValue = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_attribute_values", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_attribute_values_attribute_definitions_AttributeDefini~",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "attribute_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_attribute_values_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_barcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BarcodeType = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_barcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_item_barcodes_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_barcodes_units_of_measure_UomId",
                        column: x => x.UomId,
                        principalTable: "units_of_measure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_uoms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UomId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Length = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Width = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Height = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    Weight = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    IsReceivingUom = table.Column<bool>(type: "boolean", nullable: false),
                    IsPickingUom = table.Column<bool>(type: "boolean", nullable: false),
                    IsShippingUom = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_item_uoms", x => x.Id);
                    table.CheckConstraint("ck_item_uoms_conversion_positive", "\"ConversionQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_item_uoms_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_item_uoms_units_of_measure_UomId",
                        column: x => x.UomId,
                        principalTable: "units_of_measure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ManufactureDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpirationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupplierLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_lots_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_details",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UomId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiredLotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequiredSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MinimumShelfLifeDays = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_details", x => x.Id);
                    table.CheckConstraint("ck_order_details_ordered_positive", "\"OrderedQuantity\" > 0");
                    table.CheckConstraint("ck_order_details_quantity_progression", "\"AllocatedQuantity\" >= 0 AND \"PickedQuantity\" >= 0 AND \"ShippedQuantity\" >= 0 AND \"AllocatedQuantity\" <= \"OrderedQuantity\" AND \"PickedQuantity\" <= \"AllocatedQuantity\" AND \"ShippedQuantity\" <= \"PickedQuantity\"");
                    table.ForeignKey(
                        name: "FK_order_details_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_details_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_details_units_of_measure_UomId",
                        column: x => x.UomId,
                        principalTable: "units_of_measure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OrderDetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderType = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Carrier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ServiceLevel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OrderDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequiredShipDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ShippedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ShippedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    OrderLineCount = table.Column<int>(type: "integer", nullable: false),
                    PickedFromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FulfillmentDurationSeconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_history_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "license_plates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentLicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateType = table.Column<int>(type: "integer", nullable: false),
                    CurrentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_plates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_license_plates_license_plates_ParentLicensePlateId",
                        column: x => x.ParentLicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_license_plates_locations_CurrentLocationId",
                        column: x => x.CurrentLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_license_plates_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    InboundOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReceivingLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipts_inbound_orders_InboundOrderId",
                        column: x => x.InboundOrderId,
                        principalTable: "inbound_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_locations_ReceivingLocationId",
                        column: x => x.ReceivingLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipts_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "slotting_recommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecommendedLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    RecommendationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AlgorithmName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AlgorithmVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WasAccepted = table.Column<bool>(type: "boolean", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slotting_recommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_slotting_recommendations_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_slotting_recommendations_locations_CurrentLocationId",
                        column: x => x.CurrentLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_slotting_recommendations_locations_RecommendedLocationId",
                        column: x => x.RecommendedLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serial_numbers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Serial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_serial_numbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_serial_numbers_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_serial_numbers_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "shipment_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderDetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_lines", x => x.Id);
                    table.CheckConstraint("ck_shipment_lines_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_shipment_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_lines_order_details_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "order_details",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shipment_lines_shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventory_balances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryStatusId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    OnHandQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    HoldQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, computedColumnSql: "\"OnHandQuantity\" - \"AllocatedQuantity\" - \"HoldQuantity\"", stored: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastMovementAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_balances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_balances_inventory_statuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "inventory_statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_license_plates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_balances_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromInventoryStatusId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToInventoryStatusId = table.Column<Guid>(type: "uuid", nullable: true),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransactionType = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReferenceType = table.Column<int>(type: "integer", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PerformedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_transactions", x => x.Id);
                    table.CheckConstraint("ck_inventory_transactions_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_inventory_transactions_inventory_statuses_FromInventoryStat~",
                        column: x => x.FromInventoryStatusId,
                        principalTable: "inventory_statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_inventory_statuses_ToInventoryStatus~",
                        column: x => x.ToInventoryStatusId,
                        principalTable: "inventory_statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_license_plates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_locations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transactions_warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipt_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    InboundOrderDetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ReceivedUomId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryStatusId = table.Column<Guid>(type: "uuid", nullable: false),
                    PutawayQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_lines", x => x.Id);
                    table.CheckConstraint("ck_receipt_lines_quantity_positive", "\"ReceivedQuantity\" > 0 AND \"BaseQuantity\" > 0");
                    table.ForeignKey(
                        name: "FK_receipt_lines_inbound_order_details_InboundOrderDetailId",
                        column: x => x.InboundOrderDetailId,
                        principalTable: "inbound_order_details",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_inventory_statuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "inventory_statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_license_plates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_receipt_lines_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_receipt_lines_units_of_measure_ReceivedUomId",
                        column: x => x.ReceivedUomId,
                        principalTable: "units_of_measure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdjustmentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryStatusId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryBalanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AdjustmentQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CountTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ApprovedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_adjustments", x => x.Id);
                    table.CheckConstraint("ck_inventory_adjustments_nonzero", "\"AdjustmentQuantity\" <> 0");
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_inventory_balances_InventoryBalanceId",
                        column: x => x.InventoryBalanceId,
                        principalTable: "inventory_balances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_inventory_statuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "inventory_statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_license_plates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_adjustments_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderDetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryBalanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocatedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    AllocationStrategy = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_allocations", x => x.Id);
                    table.CheckConstraint("ck_inventory_allocations_quantity", "\"AllocatedQuantity\" > 0 AND \"PickedQuantity\" >= 0");
                    table.ForeignKey(
                        name: "FK_inventory_allocations_inventory_balances_InventoryBalanceId",
                        column: x => x.InventoryBalanceId,
                        principalTable: "inventory_balances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_allocations_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_allocations_license_plates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_allocations_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_allocations_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_allocations_order_details_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "order_details",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_inventory_allocations_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "putaway_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuggestedLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActualLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RecommendationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AssignedTo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_putaway_tasks", x => x.Id);
                    table.CheckConstraint("ck_putaway_tasks_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_putaway_tasks_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_putaway_tasks_locations_ActualLocationId",
                        column: x => x.ActualLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_putaway_tasks_locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_putaway_tasks_locations_SuggestedLocationId",
                        column: x => x.SuggestedLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_putaway_tasks_receipt_lines_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "receipt_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "count_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryStatusId = table.Column<Guid>(type: "uuid", nullable: false),
                    LotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialId = table.Column<Guid>(type: "uuid", nullable: true),
                    LicensePlateId = table.Column<Guid>(type: "uuid", nullable: true),
                    InventoryBalanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CountedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InventoryAdjustmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_count_tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_count_tasks_count_plans_CountPlanId",
                        column: x => x.CountPlanId,
                        principalTable: "count_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_count_tasks_inventory_adjustments_InventoryAdjustmentId",
                        column: x => x.InventoryAdjustmentId,
                        principalTable: "inventory_adjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_count_tasks_inventory_balances_InventoryBalanceId",
                        column: x => x.InventoryBalanceId,
                        principalTable: "inventory_balances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_count_tasks_inventory_statuses_InventoryStatusId",
                        column: x => x.InventoryStatusId,
                        principalTable: "inventory_statuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_tasks_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_tasks_license_plates_LicensePlateId",
                        column: x => x.LicensePlateId,
                        principalTable: "license_plates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_tasks_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_tasks_lots_LotId",
                        column: x => x.LotId,
                        principalTable: "lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_count_tasks_serial_numbers_SerialId",
                        column: x => x.SerialId,
                        principalTable: "serial_numbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pick_tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderDetailId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PickedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    PickBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pick_tasks", x => x.Id);
                    table.CheckConstraint("ck_pick_tasks_quantity", "\"Quantity\" > 0 AND \"PickedQuantity\" >= 0 AND \"PickedQuantity\" <= \"Quantity\"");
                    table.ForeignKey(
                        name: "FK_pick_tasks_inventory_allocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "inventory_allocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pick_tasks_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pick_tasks_locations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pick_tasks_locations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pick_tasks_order_details_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "order_details",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pick_tasks_orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "picking_route_stops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PickingPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    StopSequence = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PickTaskId = table.Column<Guid>(type: "uuid", nullable: true),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DistanceFromPrevious = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    ArrivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DepartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_picking_route_stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_picking_route_stops_locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_picking_route_stops_pick_tasks_PickTaskId",
                        column: x => x.PickTaskId,
                        principalTable: "pick_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_picking_route_stops_picking_plans_PickingPlanId",
                        column: x => x.PickingPlanId,
                        principalTable: "picking_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Code",
                table: "accounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_algorithm_configurations_AccountId_WarehouseId_AlgorithmNam~",
                table: "algorithm_configurations",
                columns: new[] { "AccountId", "WarehouseId", "AlgorithmName", "ParameterKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attribute_definitions_AccountId_Code",
                table: "attribute_definitions",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attribute_definitions_AccountId_IsPickingRelevant",
                table: "attribute_definitions",
                columns: new[] { "AccountId", "IsPickingRelevant" });

            migrationBuilder.CreateIndex(
                name: "IX_attribute_definitions_AccountId_IsSlottingRelevant",
                table: "attribute_definitions",
                columns: new[] { "AccountId", "IsSlottingRelevant" });

            migrationBuilder.CreateIndex(
                name: "IX_count_plans_AccountId_PlanNumber",
                table: "count_plans",
                columns: new[] { "AccountId", "PlanNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_count_plans_ItemId",
                table: "count_plans",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_count_plans_WarehouseId_Status",
                table: "count_plans",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_count_plans_ZoneId",
                table: "count_plans",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_CountPlanId_Status",
                table: "count_tasks",
                columns: new[] { "CountPlanId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_InventoryAdjustmentId",
                table: "count_tasks",
                column: "InventoryAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_InventoryBalanceId",
                table: "count_tasks",
                column: "InventoryBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_InventoryStatusId",
                table: "count_tasks",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_ItemId",
                table: "count_tasks",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_LicensePlateId",
                table: "count_tasks",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_LocationId",
                table: "count_tasks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_LotId",
                table: "count_tasks",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_SerialId",
                table: "count_tasks",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_count_tasks_WarehouseId_Status",
                table: "count_tasks",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_AccountId_Code",
                table: "customers",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_details_InboundOrderId_LineNumber",
                table: "inbound_order_details",
                columns: new[] { "InboundOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_details_ItemId",
                table: "inbound_order_details",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_details_UomId",
                table: "inbound_order_details",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_orders_AccountId_OrderNumber",
                table: "inbound_orders",
                columns: new[] { "AccountId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbound_orders_SupplierId",
                table: "inbound_orders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_orders_WarehouseId_Status",
                table: "inbound_orders",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_AccountId_AdjustmentNumber",
                table: "inventory_adjustments",
                columns: new[] { "AccountId", "AdjustmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_CountTaskId",
                table: "inventory_adjustments",
                column: "CountTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_InventoryBalanceId",
                table: "inventory_adjustments",
                column: "InventoryBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_InventoryStatusId",
                table: "inventory_adjustments",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_ItemId",
                table: "inventory_adjustments",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_LicensePlateId",
                table: "inventory_adjustments",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_LocationId",
                table: "inventory_adjustments",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_LotId",
                table: "inventory_adjustments",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_SerialId",
                table: "inventory_adjustments",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_adjustments_WarehouseId_Status",
                table: "inventory_adjustments",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_InventoryBalanceId",
                table: "inventory_allocations",
                column: "InventoryBalanceId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_ItemId",
                table: "inventory_allocations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_LicensePlateId",
                table: "inventory_allocations",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_LocationId",
                table: "inventory_allocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_LotId",
                table: "inventory_allocations",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_OrderDetailId",
                table: "inventory_allocations",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_SerialId",
                table: "inventory_allocations",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_allocations_Status",
                table: "inventory_allocations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_InventoryStatusId",
                table: "inventory_balances",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_ItemId_InventoryStatusId",
                table: "inventory_balances",
                columns: new[] { "ItemId", "InventoryStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_ItemId_ReceivedAt",
                table: "inventory_balances",
                columns: new[] { "ItemId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LicensePlateId",
                table: "inventory_balances",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LocationId",
                table: "inventory_balances",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_LotId",
                table: "inventory_balances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_SerialId",
                table: "inventory_balances",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_WarehouseId_ItemId",
                table: "inventory_balances",
                columns: new[] { "WarehouseId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_balances_WarehouseId_LocationId",
                table: "inventory_balances",
                columns: new[] { "WarehouseId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_statuses_Code",
                table: "inventory_statuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_CorrelationId",
                table: "inventory_transactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_FromInventoryStatusId",
                table: "inventory_transactions",
                column: "FromInventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_FromLocationId",
                table: "inventory_transactions",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ItemId_CreatedAt",
                table: "inventory_transactions",
                columns: new[] { "ItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_LicensePlateId",
                table: "inventory_transactions",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_LotId",
                table: "inventory_transactions",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ReferenceType_ReferenceId",
                table: "inventory_transactions",
                columns: new[] { "ReferenceType", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_SerialId",
                table: "inventory_transactions",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ToInventoryStatusId",
                table: "inventory_transactions",
                column: "ToInventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_ToLocationId",
                table: "inventory_transactions",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transactions_WarehouseId_CreatedAt",
                table: "inventory_transactions",
                columns: new[] { "WarehouseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_item_attribute_values_AttributeDefinitionId_TextValue",
                table: "item_attribute_values",
                columns: new[] { "AttributeDefinitionId", "TextValue" });

            migrationBuilder.CreateIndex(
                name: "IX_item_attribute_values_ItemId_AttributeDefinitionId",
                table: "item_attribute_values",
                columns: new[] { "ItemId", "AttributeDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_barcodes_Barcode",
                table: "item_barcodes",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_barcodes_ItemId",
                table: "item_barcodes",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_item_barcodes_UomId",
                table: "item_barcodes",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_item_categories_AccountId_Code",
                table: "item_categories",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_categories_ParentCategoryId",
                table: "item_categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_item_uoms_ItemId_UomId",
                table: "item_uoms",
                columns: new[] { "ItemId", "UomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_item_uoms_UomId",
                table: "item_uoms",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_items_AccountId_IsActive",
                table: "items",
                columns: new[] { "AccountId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_items_AccountId_Sku",
                table: "items",
                columns: new[] { "AccountId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_items_BaseUomId",
                table: "items",
                column: "BaseUomId");

            migrationBuilder.CreateIndex(
                name: "IX_items_CategoryId",
                table: "items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_items_DefaultPickZoneId",
                table: "items",
                column: "DefaultPickZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_items_DefaultPutawayZoneId",
                table: "items",
                column: "DefaultPutawayZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_license_plates_CurrentLocationId",
                table: "license_plates",
                column: "CurrentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_license_plates_ParentLicensePlateId",
                table: "license_plates",
                column: "ParentLicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_license_plates_WarehouseId_Code",
                table: "license_plates",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_location_profiles_AccountId_Code",
                table: "location_profiles",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_location_profiles_AllowedItemCategoryId",
                table: "location_profiles",
                column: "AllowedItemCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_LocationProfileId",
                table: "locations",
                column: "LocationProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_locations_WarehouseId_Code",
                table: "locations",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_locations_WarehouseId_IsActive_IsPutawayAllowed",
                table: "locations",
                columns: new[] { "WarehouseId", "IsActive", "IsPutawayAllowed" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_WarehouseId_PickSequence",
                table: "locations",
                columns: new[] { "WarehouseId", "PickSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_WarehouseId_ZoneId",
                table: "locations",
                columns: new[] { "WarehouseId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_locations_ZoneId",
                table: "locations",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_lots_ItemId_ExpirationDate",
                table: "lots",
                columns: new[] { "ItemId", "ExpirationDate" });

            migrationBuilder.CreateIndex(
                name: "IX_lots_ItemId_LotNumber",
                table: "lots",
                columns: new[] { "ItemId", "LotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_details_ItemId",
                table: "order_details",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_order_details_OrderId_LineNumber",
                table: "order_details",
                columns: new[] { "OrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_details_UomId",
                table: "order_details",
                column: "UomId");

            migrationBuilder.CreateIndex(
                name: "IX_order_history_ItemId",
                table: "order_history",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_order_history_OrderDetailId",
                table: "order_history",
                column: "OrderDetailId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_history_WarehouseId_ItemId_ShippedAt",
                table: "order_history",
                columns: new[] { "WarehouseId", "ItemId", "ShippedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_history_WarehouseId_ShippedAt",
                table: "order_history",
                columns: new[] { "WarehouseId", "ShippedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_AccountId_OrderNumber",
                table: "orders",
                columns: new[] { "AccountId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_CustomerId",
                table: "orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_WarehouseId_Priority_RequiredShipDate",
                table: "orders",
                columns: new[] { "WarehouseId", "Priority", "RequiredShipDate" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_WarehouseId_Status",
                table: "orders",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_AllocationId",
                table: "pick_tasks",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_DestinationLocationId",
                table: "pick_tasks",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_FromLocationId",
                table: "pick_tasks",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_ItemId",
                table: "pick_tasks",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_OrderDetailId",
                table: "pick_tasks",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_OrderId",
                table: "pick_tasks",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_PickBatchId_SequenceNumber",
                table: "pick_tasks",
                columns: new[] { "PickBatchId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_pick_tasks_WarehouseId_Status",
                table: "pick_tasks",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_picking_plans_PlanNumber",
                table: "picking_plans",
                column: "PlanNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_picking_plans_WarehouseId_GeneratedAt",
                table: "picking_plans",
                columns: new[] { "WarehouseId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_picking_route_stops_LocationId",
                table: "picking_route_stops",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_picking_route_stops_PickingPlanId_StopSequence",
                table: "picking_route_stops",
                columns: new[] { "PickingPlanId", "StopSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_picking_route_stops_PickTaskId",
                table: "picking_route_stops",
                column: "PickTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_putaway_tasks_ActualLocationId",
                table: "putaway_tasks",
                column: "ActualLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_putaway_tasks_FromLocationId",
                table: "putaway_tasks",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_putaway_tasks_ItemId",
                table: "putaway_tasks",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_putaway_tasks_ReceiptLineId",
                table: "putaway_tasks",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_putaway_tasks_SuggestedLocationId",
                table: "putaway_tasks",
                column: "SuggestedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_putaway_tasks_WarehouseId_Status",
                table: "putaway_tasks",
                columns: new[] { "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_InboundOrderDetailId",
                table: "receipt_lines",
                column: "InboundOrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_InventoryStatusId",
                table: "receipt_lines",
                column: "InventoryStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_ItemId",
                table: "receipt_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_LicensePlateId",
                table: "receipt_lines",
                column: "LicensePlateId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_LotId",
                table: "receipt_lines",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_ReceiptId",
                table: "receipt_lines",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_ReceivedUomId",
                table: "receipt_lines",
                column: "ReceivedUomId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_lines_SerialId",
                table: "receipt_lines",
                column: "SerialId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_CorrelationId",
                table: "receipts",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_InboundOrderId",
                table: "receipts",
                column: "InboundOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_ReceiptNumber",
                table: "receipts",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_receipts_ReceivingLocationId",
                table: "receipts",
                column: "ReceivingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_WarehouseId",
                table: "receipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_ItemId_Serial",
                table: "serial_numbers",
                columns: new[] { "ItemId", "Serial" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_serial_numbers_LotId",
                table: "serial_numbers",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_ItemId",
                table: "shipment_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_OrderDetailId",
                table: "shipment_lines",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_lines_ShipmentId",
                table: "shipment_lines",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_CorrelationId",
                table: "shipments",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_OrderId",
                table: "shipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_shipments_ShipmentNumber",
                table: "shipments",
                column: "ShipmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_slotting_recommendations_CurrentLocationId",
                table: "slotting_recommendations",
                column: "CurrentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_slotting_recommendations_ItemId",
                table: "slotting_recommendations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_slotting_recommendations_RecommendedLocationId",
                table: "slotting_recommendations",
                column: "RecommendedLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_slotting_recommendations_WarehouseId_ItemId_GeneratedAt",
                table: "slotting_recommendations",
                columns: new[] { "WarehouseId", "ItemId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_AccountId_Code",
                table: "suppliers",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_units_of_measure_Code",
                table: "units_of_measure",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_AccountId",
                table: "users",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouses_AccountId_Code",
                table: "warehouses",
                columns: new[] { "AccountId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_zones_WarehouseId_Code",
                table: "zones",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_zones_WarehouseId_ZoneType",
                table: "zones",
                columns: new[] { "WarehouseId", "ZoneType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "algorithm_configurations");

            migrationBuilder.DropTable(
                name: "count_tasks");

            migrationBuilder.DropTable(
                name: "inventory_transactions");

            migrationBuilder.DropTable(
                name: "item_attribute_values");

            migrationBuilder.DropTable(
                name: "item_barcodes");

            migrationBuilder.DropTable(
                name: "item_uoms");

            migrationBuilder.DropTable(
                name: "order_history");

            migrationBuilder.DropTable(
                name: "picking_route_stops");

            migrationBuilder.DropTable(
                name: "putaway_tasks");

            migrationBuilder.DropTable(
                name: "shipment_lines");

            migrationBuilder.DropTable(
                name: "slotting_recommendations");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "count_plans");

            migrationBuilder.DropTable(
                name: "inventory_adjustments");

            migrationBuilder.DropTable(
                name: "attribute_definitions");

            migrationBuilder.DropTable(
                name: "pick_tasks");

            migrationBuilder.DropTable(
                name: "picking_plans");

            migrationBuilder.DropTable(
                name: "receipt_lines");

            migrationBuilder.DropTable(
                name: "shipments");

            migrationBuilder.DropTable(
                name: "inventory_allocations");

            migrationBuilder.DropTable(
                name: "inbound_order_details");

            migrationBuilder.DropTable(
                name: "receipts");

            migrationBuilder.DropTable(
                name: "inventory_balances");

            migrationBuilder.DropTable(
                name: "order_details");

            migrationBuilder.DropTable(
                name: "inbound_orders");

            migrationBuilder.DropTable(
                name: "inventory_statuses");

            migrationBuilder.DropTable(
                name: "license_plates");

            migrationBuilder.DropTable(
                name: "serial_numbers");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "lots");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "location_profiles");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "item_categories");

            migrationBuilder.DropTable(
                name: "units_of_measure");

            migrationBuilder.DropTable(
                name: "zones");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
