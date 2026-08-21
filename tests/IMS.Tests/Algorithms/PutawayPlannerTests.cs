using FluentAssertions;
using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Application.Features.Algorithms;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using IMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IMS.Tests.Algorithms;

/// <summary>
/// The planner is the one place in the system that makes a decision rather than recording
/// one, so what is asserted here is the decision itself: which locations come back, in what
/// order, and how the requested quantity is split across them.
/// </summary>
public class PutawayPlannerTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid WarehouseId = Guid.NewGuid();

    // --- Harness ------------------------------------------------------------

    private sealed class StubCurrentUser : ICurrentUser
    {
        public Guid? AccountId => PutawayPlannerTests.AccountId;
        public Guid? UserId => Guid.Empty;
        public string? Username => "tester";
        public UserRole? Role => UserRole.Admin;
        public bool IsAuthenticated => true;
        public bool IsInRole(params UserRole[] roles) => true;
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    }

    private static ImsDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ImsDbContext>()
            .UseInMemoryDatabase($"putaway-{Guid.NewGuid()}")
            .Options;

        return new ImsDbContext(options, new StubCurrentUser(), new FixedClock());
    }

    private static PutawayPlanner NewPlanner(ImsDbContext db) =>
        new(db, new ScopeGuard(db, new StubCurrentUser()));

    /// <summary>A warehouse with one reserve zone and one picking zone, and nothing in stock.</summary>
    private static async Task<ImsDbContext> SeedAsync(
        Action<ImsDbContext, Zone, Zone> addLocations,
        ItemMaster item)
    {
        var db = NewContext();

        db.Accounts.Add(new Account { Id = AccountId, Code = "ACC", Name = "Test Account" });
        db.Warehouses.Add(new Warehouse
        {
            Id = WarehouseId, AccountId = AccountId, Code = "WH01", Name = "Test Depot"
        });

        var reserve = new Zone
        {
            Id = Guid.NewGuid(), WarehouseId = WarehouseId, Code = "RSV",
            Name = "Reserve", ZoneType = ZoneType.ReserveStorage, Priority = 2
        };

        var picking = new Zone
        {
            Id = Guid.NewGuid(), WarehouseId = WarehouseId, Code = "PCK",
            Name = "Picking", ZoneType = ZoneType.Picking, Priority = 1
        };

        db.Zones.AddRange(reserve, picking);
        db.Items.Add(item);

        addLocations(db, reserve, picking);

        await db.SaveChangesAsync();
        return db;
    }

    private static Location NewLocation(
        Zone zone, string code, decimal? maxWeight = null, decimal? maxVolume = null,
        LocationType type = LocationType.StandardShelf,
        decimal? distanceToPacking = null, int? accessibility = null) => new()
        {
            Id = Guid.NewGuid(),
            WarehouseId = WarehouseId,
            ZoneId = zone.Id,
            Zone = zone,
            Code = code,
            LocationType = type,
            MaxWeight = maxWeight,
            MaxVolume = maxVolume,
            DistanceToPacking = distanceToPacking,
            AccessibilityScore = accessibility,
            IsPickable = true,
            IsPutawayAllowed = true,
            IsActive = true
        };

    private static ItemMaster NewItem(
        decimal? weight = null, decimal? volume = null,
        bool hazardous = false, bool temperatureControlled = false) => new()
        {
            Id = Guid.NewGuid(),
            AccountId = AccountId,
            Sku = "SKU-1",
            Name = "Test Item",
            BaseUomId = Guid.NewGuid(),
            Weight = weight,
            Volume = volume,
            IsHazardous = hazardous,
            IsTemperatureControlled = temperatureControlled
        };

    // --- Capacity -----------------------------------------------------------

    [Fact]
    public async Task Splits_the_quantity_across_locations_when_one_cannot_hold_it_all()
    {
        var item = NewItem(weight: 10m);
        Location first = null!, second = null!;

        using var db = await SeedAsync((context, reserve, _) =>
        {
            // 60 kg holds 6 units, 200 kg holds 20 - together more than the 8 requested.
            first = NewLocation(reserve, "A-01", maxWeight: 60m, accessibility: 90);
            second = NewLocation(reserve, "A-02", maxWeight: 200m, accessibility: 10);
            context.Locations.AddRange(first, second);
        }, item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 8m);

        plan.PlannedQuantity.Should().Be(8m);
        plan.UnplannedQuantity.Should().Be(0m);
        plan.Lines.Should().HaveCount(2);

        // The more accessible location is filled to its ceiling first, the rest overflows.
        plan.Lines[0].LocationCode.Should().Be("A-01");
        plan.Lines[0].Quantity.Should().Be(6m);
        plan.Lines[0].UnitsThatFit.Should().Be(6m);
        plan.Lines[1].LocationCode.Should().Be("A-02");
        plan.Lines[1].Quantity.Should().Be(2m);
    }

    [Fact]
    public async Task Counts_stock_already_in_the_location_against_its_capacity()
    {
        var item = NewItem(weight: 10m);
        Location location = null!;

        using var db = await SeedAsync((context, reserve, _) =>
        {
            location = NewLocation(reserve, "A-01", maxWeight: 100m);
            context.Locations.Add(location);
        }, item);

        // 7 units already there weigh 70 kg, leaving room for 3 more.
        db.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            WarehouseId = WarehouseId,
            LocationId = location.Id,
            ItemId = item.Id,
            InventoryStatusId = Guid.NewGuid(),
            OnHandQuantity = 7m
        });
        await db.SaveChangesAsync();

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 10m);

        plan.Lines.Should().ContainSingle();
        plan.Lines[0].UnitsThatFit.Should().Be(3m);
        plan.Lines[0].RemainingWeight.Should().Be(30m);
        plan.Lines[0].AlreadyHoldsItem.Should().BeTrue();
        plan.PlannedQuantity.Should().Be(3m);
        plan.UnplannedQuantity.Should().Be(7m);
        plan.Notes.Should().Contain(n => n.Contains("could not be placed"));
    }

    [Fact]
    public async Task Takes_whichever_of_weight_and_volume_runs_out_first()
    {
        // Weight allows 10, volume allows only 4.
        var item = NewItem(weight: 1m, volume: 0.5m);

        using var db = await SeedAsync((context, reserve, _) =>
            context.Locations.Add(NewLocation(reserve, "A-01", maxWeight: 10m, maxVolume: 2m)),
            item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 10m);

        plan.Lines[0].UnitsThatFit.Should().Be(4m);
        plan.PlannedQuantity.Should().Be(4m);
    }

    [Fact]
    public async Task Reports_no_limit_rather_than_inventing_one_when_capacity_is_undeclared()
    {
        var item = NewItem(weight: 10m);

        using var db = await SeedAsync((context, reserve, _) =>
            context.Locations.Add(NewLocation(reserve, "A-01")), item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 500m);

        plan.Lines[0].UnitsThatFit.Should().BeNull();
        plan.PlannedQuantity.Should().Be(500m);
        plan.UnplannedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task Says_so_when_the_item_carries_neither_weight_nor_volume()
    {
        var item = NewItem();

        using var db = await SeedAsync((context, reserve, _) =>
            context.Locations.Add(NewLocation(reserve, "A-01", maxWeight: 100m)), item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 5m);

        plan.Notes.Should().Contain(n => n.Contains("neither a weight nor a volume"));
        plan.Lines[0].UnitsThatFit.Should().BeNull();
    }

    // --- Eligibility --------------------------------------------------------

    [Fact]
    public async Task Refuses_to_suggest_a_location_that_cannot_legally_hold_the_item()
    {
        var item = NewItem(weight: 1m, hazardous: true);

        using var db = await SeedAsync((context, reserve, _) =>
            context.Locations.Add(NewLocation(reserve, "A-01", maxWeight: 100m)), item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 5m);

        plan.Lines.Should().BeEmpty();
        plan.UnplannedQuantity.Should().Be(5m);
        plan.Notes.Should().Contain(n => n.Contains("No location in this warehouse may hold"));
    }

    [Fact]
    public async Task Sends_hazardous_stock_to_a_hazmat_capable_location()
    {
        var item = NewItem(weight: 1m, hazardous: true);

        using var db = await SeedAsync((context, reserve, _) =>
        {
            context.Locations.Add(NewLocation(reserve, "A-01"));
            context.Locations.Add(NewLocation(reserve, "DG-01", type: LocationType.DangerousGoods));
        }, item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 5m);

        plan.Lines.Should().ContainSingle();
        plan.Lines[0].LocationCode.Should().Be("DG-01");
    }

    [Fact]
    public async Task Skips_locations_that_are_closed_to_putaway()
    {
        var item = NewItem(weight: 1m);

        using var db = await SeedAsync((context, reserve, _) =>
        {
            var blocked = NewLocation(reserve, "A-01");
            blocked.IsPutawayAllowed = false;
            context.Locations.Add(blocked);
            context.Locations.Add(NewLocation(reserve, "A-02"));
        }, item);

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 5m);

        plan.Lines.Should().ContainSingle();
        plan.Lines[0].LocationCode.Should().Be("A-02");
    }

    // --- Ranking ------------------------------------------------------------

    [Fact]
    public async Task Prefers_a_location_that_already_holds_the_same_item()
    {
        var item = NewItem(weight: 1m);
        Location empty = null!, holding = null!;

        using var db = await SeedAsync((context, reserve, _) =>
        {
            // The empty location is the more accessible one, so only consolidation can
            // explain the holding location coming first.
            empty = NewLocation(reserve, "A-01", accessibility: 100);
            holding = NewLocation(reserve, "A-02", accessibility: 10);
            context.Locations.AddRange(empty, holding);
        }, item);

        db.InventoryBalances.Add(new InventoryBalance
        {
            Id = Guid.NewGuid(),
            WarehouseId = WarehouseId,
            LocationId = holding.Id,
            ItemId = item.Id,
            InventoryStatusId = Guid.NewGuid(),
            OnHandQuantity = 2m
        });
        await db.SaveChangesAsync();

        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 3m);

        plan.Lines[0].LocationCode.Should().Be("A-02");
        plan.Lines[0].AlreadyHoldsItem.Should().BeTrue();
        plan.Lines[0].Reason.Should().Contain("already holds this item");
    }

    [Fact]
    public async Task Puts_a_slow_mover_in_reserve_storage()
    {
        var item = NewItem(weight: 1m);

        using var db = await SeedAsync((context, reserve, picking) =>
        {
            context.Locations.Add(NewLocation(picking, "PCK-01", type: LocationType.PickFace));
            context.Locations.Add(NewLocation(reserve, "RSV-01", type: LocationType.ReserveStorage));
        }, item);

        // No shipping history at all, so the item is a slow mover by definition.
        var plan = await NewPlanner(db).PlanAsync(WarehouseId, item.Id, 1m);

        plan.IsFastMover.Should().BeFalse();
        plan.Lines[0].LocationCode.Should().Be("RSV-01");
        plan.Lines[0].Reason.Should().Contain("reserve storage");
    }

    [Fact]
    public async Task Rejects_a_quantity_of_zero()
    {
        var item = NewItem(weight: 1m);

        using var db = await SeedAsync((context, reserve, _) =>
            context.Locations.Add(NewLocation(reserve, "A-01")), item);

        var act = () => NewPlanner(db).PlanAsync(WarehouseId, item.Id, 0m);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }
}
