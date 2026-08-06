using FluentAssertions;
using IMS.Domain.Entities.Counting;
using IMS.Domain.Entities.Inbound;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Xunit;

namespace IMS.Tests.Domain;

/// <summary>Doc §6.1 - inbound order lifecycle.</summary>
public class InboundOrderLifecycleTests
{
    [Theory]
    [InlineData(InboundOrderStatus.Draft, InboundOrderStatus.Expected)]
    [InlineData(InboundOrderStatus.Expected, InboundOrderStatus.PartiallyReceived)]
    [InlineData(InboundOrderStatus.Expected, InboundOrderStatus.Received)]
    [InlineData(InboundOrderStatus.Received, InboundOrderStatus.Completed)]
    public void Documented_transitions_are_allowed(InboundOrderStatus from, InboundOrderStatus to)
        => new InboundOrder { Status = from }.CanTransitionTo(to).Should().BeTrue();

    [Theory]
    [InlineData(InboundOrderStatus.Draft, InboundOrderStatus.Received)]
    [InlineData(InboundOrderStatus.Completed, InboundOrderStatus.Expected)]
    [InlineData(InboundOrderStatus.Cancelled, InboundOrderStatus.Received)]
    public void Undocumented_transitions_are_refused(InboundOrderStatus from, InboundOrderStatus to)
    {
        var order = new InboundOrder { Status = from };

        order.CanTransitionTo(to).Should().BeFalse();
        new Action(() => order.TransitionTo(to)).Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Header_becomes_PartiallyReceived_when_only_some_lines_arrive()
    {
        var order = new InboundOrder { Status = InboundOrderStatus.Expected };
        order.Details.Add(new InboundOrderDetail { ExpectedQuantity = 10, ReceivedQuantity = 4 });
        order.Details.Add(new InboundOrderDetail { ExpectedQuantity = 10, ReceivedQuantity = 0 });

        order.RecalculateStatus();

        order.Status.Should().Be(InboundOrderStatus.PartiallyReceived);
    }

    [Fact]
    public void Header_becomes_Received_only_when_every_line_is_complete()
    {
        var order = new InboundOrder { Status = InboundOrderStatus.Expected };
        order.Details.Add(new InboundOrderDetail { ExpectedQuantity = 10, ReceivedQuantity = 10 });
        order.Details.Add(new InboundOrderDetail { ExpectedQuantity = 5, ReceivedQuantity = 5 });

        order.RecalculateStatus();

        order.Status.Should().Be(InboundOrderStatus.Received);
    }
}

/// <summary>Doc §7.1 - outbound order lifecycle, and acceptance scenario 3.</summary>
public class OrderLifecycleTests
{
    private static OrderMaster OrderWith(params (decimal ordered, decimal allocated)[] lines)
    {
        var order = new OrderMaster { Status = OrderStatus.Released };

        foreach (var (ordered, allocated) in lines)
            order.Details.Add(new OrderDetail { OrderedQuantity = ordered, AllocatedQuantity = allocated });

        return order;
    }

    [Fact]
    public void Order_reaches_Allocated_only_when_every_line_is_fully_covered()
    {
        var order = OrderWith((10, 10), (5, 5));

        order.RecalculateAllocationStatus();

        order.Status.Should().Be(OrderStatus.Allocated);
    }

    [Fact]
    public void A_short_line_keeps_the_order_PartiallyAllocated()
    {
        // Acceptance scenario 3: the order must NOT become fully allocated.
        var order = OrderWith((10, 10), (100, 4));

        order.RecalculateAllocationStatus();

        order.Status.Should().Be(OrderStatus.PartiallyAllocated);
    }

    [Fact]
    public void An_order_with_nothing_allocated_stays_Released()
    {
        var order = OrderWith((10, 0));

        order.RecalculateAllocationStatus();

        order.Status.Should().Be(OrderStatus.Released);
    }

    [Fact]
    public void Cancelled_lines_do_not_block_full_allocation()
    {
        var order = OrderWith((10, 10));
        order.Details.Add(new OrderDetail
        {
            OrderedQuantity = 99, AllocatedQuantity = 0, Status = OrderDetailStatus.Cancelled
        });

        order.RecalculateAllocationStatus();

        order.Status.Should().Be(OrderStatus.Allocated);
    }

    [Fact]
    public void Shipping_before_picking_is_refused_by_the_lifecycle()
    {
        var order = new OrderMaster { Status = OrderStatus.Allocated };

        new Action(() => order.TransitionTo(OrderStatus.Shipped))
            .Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void A_shipped_order_is_terminal()
    {
        var order = new OrderMaster { Status = OrderStatus.Shipped };

        new Action(() => order.TransitionTo(OrderStatus.Shipped))
            .Should().Throw<InvalidStateTransitionException>();
        new Action(() => order.TransitionTo(OrderStatus.Cancelled))
            .Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Line_status_tracks_its_own_progress()
    {
        var line = new OrderDetail { OrderedQuantity = 10 };

        line.RecalculateStatus();
        line.Status.Should().Be(OrderDetailStatus.Open);

        line.AllocatedQuantity = 4;
        line.RecalculateStatus();
        line.Status.Should().Be(OrderDetailStatus.PartiallyAllocated);

        line.AllocatedQuantity = 10;
        line.RecalculateStatus();
        line.Status.Should().Be(OrderDetailStatus.Allocated);

        line.PickedQuantity = 10;
        line.RecalculateStatus();
        line.Status.Should().Be(OrderDetailStatus.Picked);

        line.ShippedQuantity = 10;
        line.RecalculateStatus();
        line.Status.Should().Be(OrderDetailStatus.Shipped);
    }
}

/// <summary>
/// Doc §11.8 - putaway must respect temperature compatibility.
/// The location's operating band has to sit INSIDE the item's acceptable band.
/// </summary>
public class TemperatureCompatibilityTests
{
    private static LocationProfile Profile(decimal? min, decimal? max)
        => new() { TemperatureMin = min, TemperatureMax = max };

    [Fact]
    public void An_item_with_no_temperature_requirement_fits_anywhere()
        => Profile(null, null).SupportsTemperatureRange(null, null).Should().BeTrue();

    [Fact]
    public void An_ambient_location_cannot_hold_temperature_controlled_stock()
        => Profile(null, null).SupportsTemperatureRange(2, 6).Should().BeFalse();

    [Fact]
    public void A_matching_band_fits()
        => Profile(2, 5).SupportsTemperatureRange(2, 6).Should().BeTrue();

    [Fact]
    public void A_freezer_cannot_hold_chilled_stock_even_though_both_are_cold()
    {
        // -20..-15 overlaps nothing in 2..6, and the location could freeze the goods.
        Profile(-20, -15).SupportsTemperatureRange(2, 6).Should().BeFalse();
    }

    [Fact]
    public void A_location_that_can_get_colder_than_the_item_allows_is_refused()
        => Profile(-5, 4).SupportsTemperatureRange(2, 6).Should().BeFalse();

    [Fact]
    public void A_location_that_can_get_warmer_than_the_item_allows_is_refused()
        => Profile(2, 12).SupportsTemperatureRange(2, 6).Should().BeFalse();

    [Fact]
    public void A_half_open_location_band_cannot_guarantee_a_declared_bound()
        => Profile(2, null).SupportsTemperatureRange(2, 6).Should().BeFalse();
}

/// <summary>Doc §5.3 - lot expiry, the groundwork for FEFO.</summary>
public class LotTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_lot_past_its_expiration_reports_expired()
        => new Lot { ExpirationDate = Now.AddDays(-1) }.IsExpired(Now).Should().BeTrue();

    [Fact]
    public void A_lot_with_no_expiration_never_expires()
        => new Lot { ExpirationDate = null }.IsExpired(Now).Should().BeFalse();

    [Fact]
    public void Remaining_shelf_life_is_reported_in_whole_days()
        => new Lot { ExpirationDate = Now.AddDays(14) }
            .RemainingShelfLifeDays(Now).Should().Be(14);
}

/// <summary>
/// Faz 5 - the reconstructed §9 count task. Section 9 is missing from the source
/// document; these pin the reconstruction down.
/// </summary>
public class CountTaskTests
{
    [Fact]
    public void A_matching_count_reports_no_variance()
    {
        var task = new CountTask { SystemQuantity = 50, CountedQuantity = 50 };

        task.Variance.Should().Be(0);
        task.HasVariance.Should().BeFalse();
    }

    [Fact]
    public void A_short_count_reports_a_negative_variance()
    {
        var task = new CountTask { SystemQuantity = 50, CountedQuantity = 43 };

        task.Variance.Should().Be(-7);
        task.HasVariance.Should().BeTrue();
    }

    [Fact]
    public void An_uncounted_task_has_no_variance_yet()
    {
        var task = new CountTask { SystemQuantity = 50 };

        task.Variance.Should().BeNull();
        task.HasVariance.Should().BeFalse();
    }

    [Fact]
    public void A_variance_task_must_be_resolved_before_it_closes()
    {
        var task = new CountTask { Status = CountTaskStatus.VarianceFound };

        // It may only move to Completed (adjustment decided) or Cancelled.
        new Action(() => task.TransitionTo(CountTaskStatus.Counted))
            .Should().Throw<InvalidStateTransitionException>();

        task.TransitionTo(CountTaskStatus.Completed);
        task.Status.Should().Be(CountTaskStatus.Completed);
    }

    [Fact]
    public void An_approved_adjustment_is_terminal()
    {
        var adjustment = new InventoryAdjustment { Status = AdjustmentStatus.Approved };

        new Action(() => adjustment.TransitionTo(AdjustmentStatus.Approved))
            .Should().Throw<InvalidStateTransitionException>();
        new Action(() => adjustment.TransitionTo(AdjustmentStatus.Rejected))
            .Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void A_positive_adjustment_is_reported_as_an_increase()
    {
        new InventoryAdjustment { AdjustmentQuantity = 4 }.IsIncrease.Should().BeTrue();
        new InventoryAdjustment { AdjustmentQuantity = -4 }.IsIncrease.Should().BeFalse();
    }
}

/// <summary>Doc §4.3 - UOM conversion, which every quantity calculation depends on.</summary>
public class ItemUomTests
{
    [Fact]
    public void Converting_to_base_multiplies_by_the_conversion()
    {
        // Doc §4.3 example: 1 Case = 24 Each.
        var caseUom = new ItemUom { ConversionQuantity = 24 };

        caseUom.ToBaseQuantity(10).Should().Be(240);
    }

    [Fact]
    public void Converting_from_base_divides_by_the_conversion()
        => new ItemUom { ConversionQuantity = 24 }.FromBaseQuantity(240).Should().Be(10);

    [Fact]
    public void A_zero_conversion_cannot_divide_by_zero()
        => new ItemUom { ConversionQuantity = 0 }.FromBaseQuantity(240).Should().Be(0);
}

/// <summary>Doc §7.4 - pick task short-pick reporting.</summary>
public class PickTaskTests
{
    [Fact]
    public void A_full_pick_reports_no_shortfall()
        => new PickTask { Quantity = 10, PickedQuantity = 10 }.ShortQuantity.Should().Be(0);

    [Fact]
    public void A_short_pick_reports_the_gap()
        => new PickTask { Quantity = 10, PickedQuantity = 6 }.ShortQuantity.Should().Be(4);

    [Fact]
    public void A_completed_task_cannot_be_completed_again()
    {
        var task = new PickTask { Status = PickTaskStatus.Completed };

        new Action(() => task.TransitionTo(PickTaskStatus.Completed))
            .Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void An_allocation_reports_what_is_still_open()
        => new InventoryAllocation { AllocatedQuantity = 10, PickedQuantity = 4 }
            .OpenQuantity.Should().Be(6);
}
