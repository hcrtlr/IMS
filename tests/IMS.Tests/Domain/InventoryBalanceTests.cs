using FluentAssertions;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Exceptions;
using Xunit;

namespace IMS.Tests.Domain;

/// <summary>
/// Doc §5.1 and the §11 rules that govern a single stock balance. These run against the
/// domain object alone - no database, no host - so the rules are pinned down independently
/// of how they happen to be persisted.
/// </summary>
public class InventoryBalanceTests
{
    private static InventoryBalance Balance(decimal onHand, decimal allocated = 0, decimal hold = 0)
        => new()
        {
            WarehouseId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            InventoryStatusId = Guid.NewGuid(),
            OnHandQuantity = onHand,
            AllocatedQuantity = allocated,
            HoldQuantity = hold
        };

    [Theory]
    [InlineData(100, 0, 0, 100)]
    [InlineData(100, 30, 0, 70)]
    [InlineData(100, 30, 20, 50)]
    [InlineData(100, 100, 0, 0)]
    public void Available_follows_the_documented_formula(
        decimal onHand, decimal allocated, decimal hold, decimal expected)
    {
        // Doc §5.1: Available = OnHand - Allocated - Hold
        Balance(onHand, allocated, hold).ComputeAvailable().Should().Be(expected);
    }

    [Fact]
    public void Allocate_raises_allocated_and_leaves_on_hand_untouched()
    {
        var balance = Balance(100);

        balance.Allocate(40, "SKU-1");

        // Rule §11.2: allocation must not change on-hand.
        balance.OnHandQuantity.Should().Be(100);
        balance.AllocatedQuantity.Should().Be(40);
        balance.ComputeAvailable().Should().Be(60);
    }

    [Fact]
    public void Allocate_beyond_available_is_refused_with_the_shortfall()
    {
        var balance = Balance(100, allocated: 80);

        // Rule §11.1: no allocation without sufficient available stock.
        var act = () => balance.Allocate(50, "SKU-1");

        var ex = act.Should().Throw<InsufficientStockException>().Which;
        ex.Shortfalls.Should().ContainSingle();
        ex.Shortfalls[0].RequestedQuantity.Should().Be(50);
        ex.Shortfalls[0].AvailableQuantity.Should().Be(20);
        ex.Shortfalls[0].ShortQuantity.Should().Be(30);

        balance.AllocatedQuantity.Should().Be(80, "a refused allocation must change nothing");
    }

    [Fact]
    public void Held_quantity_is_not_allocatable()
    {
        var balance = Balance(100, hold: 100);

        var act = () => balance.Allocate(1, "SKU-1");

        act.Should().Throw<InsufficientStockException>();
    }

    [Fact]
    public void Ship_reduces_both_on_hand_and_allocated()
    {
        var balance = Balance(100, allocated: 40);

        balance.ShipAllocated(40, "SKU-1");

        // Rule §11.3: shipment reduces on-hand AND allocated.
        balance.OnHandQuantity.Should().Be(60);
        balance.AllocatedQuantity.Should().Be(0);
        balance.ComputeAvailable().Should().Be(60);
    }

    [Fact]
    public void Ship_more_than_is_allocated_is_refused()
    {
        var balance = Balance(100, allocated: 10);

        var act = () => balance.ShipAllocated(40, "SKU-1");

        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage("*only 10 is allocated*");
    }

    [Fact]
    public void RemoveStock_cannot_consume_allocated_or_held_quantity()
    {
        var balance = Balance(100, allocated: 60, hold: 30);

        // Only 10 is genuinely free.
        var act = () => balance.RemoveStock(20, "SKU-1");

        act.Should().Throw<InsufficientStockException>()
            .Which.Shortfalls[0].AvailableQuantity.Should().Be(10);
    }

    [Fact]
    public void RemoveStock_never_drives_the_balance_negative()
    {
        var balance = Balance(5);

        var act = () => balance.RemoveStock(6, "SKU-1");

        act.Should().Throw<InsufficientStockException>();
        balance.OnHandQuantity.Should().Be(5);
    }

    [Fact]
    public void Deallocate_returns_stock_to_available_without_touching_on_hand()
    {
        var balance = Balance(100, allocated: 40);

        balance.Deallocate(40);

        balance.OnHandQuantity.Should().Be(100);
        balance.AllocatedQuantity.Should().Be(0);
        balance.ComputeAvailable().Should().Be(100);
    }

    [Fact]
    public void Deallocating_more_than_is_reserved_is_refused()
    {
        var balance = Balance(100, allocated: 10);

        var act = () => balance.Deallocate(40);

        act.Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void Hold_blocks_allocation_without_changing_on_hand()
    {
        var balance = Balance(100);

        balance.PlaceHold(30, "SKU-1");

        balance.OnHandQuantity.Should().Be(100);
        balance.HoldQuantity.Should().Be(30);
        balance.ComputeAvailable().Should().Be(70);
    }

    [Fact]
    public void AddAllocatedStock_keeps_picked_stock_reserved_in_staging()
    {
        var staging = Balance(0);

        staging.AddAllocatedStock(25);

        // Picked stock sits in staging still committed to its order, so it is
        // invisible to any other allocation.
        staging.OnHandQuantity.Should().Be(25);
        staging.AllocatedQuantity.Should().Be(25);
        staging.ComputeAvailable().Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_quantities_are_refused_everywhere(decimal quantity)
    {
        var balance = Balance(100, allocated: 50, hold: 10);

        new Action(() => balance.AddStock(quantity)).Should().Throw<BusinessRuleViolationException>();
        new Action(() => balance.RemoveStock(quantity, "S")).Should().Throw<BusinessRuleViolationException>();
        new Action(() => balance.Allocate(quantity, "S")).Should().Throw<BusinessRuleViolationException>();
        new Action(() => balance.Deallocate(quantity)).Should().Throw<BusinessRuleViolationException>();
        new Action(() => balance.PlaceHold(quantity, "S")).Should().Throw<BusinessRuleViolationException>();
        new Action(() => balance.ReleaseHold(quantity)).Should().Throw<BusinessRuleViolationException>();
        new Action(() => balance.ShipAllocated(quantity, "S")).Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void A_fully_drained_balance_reports_itself_empty()
    {
        var balance = Balance(10);

        balance.RemoveStock(10, "SKU-1");

        balance.IsEmpty().Should().BeTrue();
    }
}
