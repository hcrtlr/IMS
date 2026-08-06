using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Application.Features.Inventory;
using IMS.Domain.Entities.Counting;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Counting;

/// <summary>
/// Faz 5 - cycle counting and stock adjustment.
///
/// SECTION 9 OF THE SOURCE DOCUMENT IS MISSING (it jumps from §8 to §10), yet counting
/// and adjustment are required by §1, the §12 API list and Faz 5. This whole workflow is
/// reconstructed by analogy with the documented inbound and outbound task patterns.
/// Every inference is recorded in docs/ASSUMPTIONS.md section E.
///
/// The design centres on being auditable end to end:
///   - a plan records who created it and when;
///   - each task records the blind system snapshot, who counted, what they found and when;
///   - an adjustment records who raised it, who approved or rejected it, and the actual
///     on-hand immediately before and after the change was applied;
///   - stock only moves on approval, and that write is a single CountAdjustment ledger
///     row linked back to the adjustment (rule §11.9).
/// </summary>
public class CountingService
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryRepository _inventory;
    private readonly InventoryLedger _ledger;
    private readonly ScopeGuard _scope;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CountingService> _logger;

    public CountingService(
        IApplicationDbContext db,
        IInventoryRepository inventory,
        InventoryLedger ledger,
        ScopeGuard scope,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<CountingService> logger)
    {
        _db = db;
        _inventory = inventory;
        _ledger = ledger;
        _scope = scope;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    // ================================================================= plans

    /// <summary>POST /api/count-plans (§12).</summary>
    public async Task<CountPlanDto> CreatePlanAsync(
        CreateCountPlanRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        var planNumber = string.IsNullOrWhiteSpace(request.PlanNumber)
            ? await GenerateNumberAsync("CP", ct)
            : request.PlanNumber;

        if (await _db.CountPlans.AnyAsync(
                p => p.AccountId == _scope.AccountId && p.PlanNumber == planNumber, ct))
            throw new DuplicateEntityException($"Count plan '{planNumber}' already exists.");

        // The selection mode decides which scope field is mandatory.
        switch (request.SelectionMode)
        {
            case CountSelectionMode.ByZone when request.ZoneId is null:
                throw new BusinessRuleViolationException("SelectionMode ByZone requires a ZoneId.");

            case CountSelectionMode.ByItem when request.ItemId is null:
                throw new BusinessRuleViolationException("SelectionMode ByItem requires an ItemId.");

            case CountSelectionMode.ByLocation
                when request.LocationIds is null || request.LocationIds.Count == 0:
                throw new BusinessRuleViolationException(
                    "SelectionMode ByLocation requires at least one location.");
        }

        if (request.ZoneId.HasValue)
        {
            var ok = await _db.Zones.AnyAsync(
                z => z.Id == request.ZoneId.Value && z.WarehouseId == request.WarehouseId, ct);

            if (!ok) throw new NotFoundException("Zone", request.ZoneId.Value);
        }

        if (request.ItemId.HasValue) await _scope.EnsureItemAsync(request.ItemId.Value, ct);

        foreach (var locationId in request.LocationIds ?? Array.Empty<Guid>())
            await _scope.EnsureLocationAsync(locationId, request.WarehouseId, ct);

        var plan = new CountPlan
        {
            AccountId = _scope.AccountId,
            WarehouseId = request.WarehouseId,
            PlanNumber = planNumber,
            Name = request.Name,
            CountType = request.CountType ?? CountType.Cycle,
            SelectionMode = request.SelectionMode,
            ZoneId = request.ZoneId,
            ItemId = request.ItemId,
            ScheduledDate = request.ScheduledDate,
            BlockAllocationDuringCount = request.BlockAllocationDuringCount,
            Notes = request.Notes,
            Status = CountPlanStatus.Draft
        };

        _db.CountPlans.Add(plan);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Count plan {PlanNumber} created by {User} ({Mode})",
            plan.PlanNumber, _scope.Username, plan.SelectionMode);

        return await GetPlanAsync(plan.Id, ct);
    }

    /// <summary>
    /// Releases a plan and generates one task per countable stock combination in scope.
    ///
    /// A task is created per InventoryBalance row, matching the §5.1 uniqueness tuple, so a
    /// variance always resolves to exactly one balance. Locations in scope that hold no
    /// stock are skipped: counting nothing produces no evidence either way.
    /// </summary>
    public async Task<CountPlanDto> ReleasePlanAsync(
        Guid planId, IReadOnlyList<Guid>? locationIds = null, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var plan = await _db.CountPlans
                .Include(p => p.Tasks)
                .FirstOrDefaultAsync(p => p.Id == planId && p.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(CountPlan), planId);

            // A ByLocation plan is scoped by an explicit list, which is supplied at release
            // time rather than held as server-side state between the two calls.
            if (plan.SelectionMode == CountSelectionMode.ByLocation
                && (locationIds is null || locationIds.Count == 0))
            {
                throw new BusinessRuleViolationException(
                    "Releasing a ByLocation count plan requires the list of locations to count.");
            }

            foreach (var locationId in locationIds ?? Array.Empty<Guid>())
                await _scope.EnsureLocationAsync(locationId, plan.WarehouseId, token);

            plan.TransitionTo(CountPlanStatus.Released);
            plan.ReleasedAt = _clock.UtcNow;

            var scoped = _db.InventoryBalances
                .Where(b => b.WarehouseId == plan.WarehouseId && b.OnHandQuantity != 0m);

            scoped = plan.SelectionMode switch
            {
                CountSelectionMode.ByZone =>
                    scoped.Where(b => b.Location.ZoneId == plan.ZoneId),

                CountSelectionMode.ByItem =>
                    scoped.Where(b => b.ItemId == plan.ItemId),

                CountSelectionMode.ByLocation =>
                    scoped.Where(b => locationIds!.Contains(b.LocationId)),

                _ => scoped
            };

            var balances = await scoped.ToListAsync(token);

            if (balances.Count == 0)
                throw new BusinessRuleViolationException(
                    "No stock was found in this plan's scope, so there is nothing to count.");

            foreach (var balance in balances)
            {
                // Added through the DbSet, not just the navigation collection. The plan is
                // already persisted and BaseEntity pre-assigns Id, so a navigation-only add
                // is classified Modified and emits an UPDATE against a non-existent row.
                var task = new CountTask
                {
                    WarehouseId = balance.WarehouseId,
                    CountPlanId = plan.Id,
                    LocationId = balance.LocationId,
                    ItemId = balance.ItemId,
                    InventoryStatusId = balance.InventoryStatusId,
                    LotId = balance.LotId,
                    SerialId = balance.SerialId,
                    LicensePlateId = balance.LicensePlateId,
                    InventoryBalanceId = balance.Id,
                    // The blind snapshot the counter is measured against.
                    SystemQuantity = balance.OnHandQuantity,
                    Status = CountTaskStatus.Created
                };

                _db.CountTasks.Add(task);
                plan.Tasks.Add(task);
            }

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Count plan {PlanNumber} released by {User}: {TaskCount} tasks generated",
                plan.PlanNumber, _scope.Username, balances.Count);

            return await GetPlanAsync(plan.Id, token);
        }, ct);
    }

    // ================================================================= tasks

    /// <summary>
    /// POST /api/count-tasks/{id}/complete (§12).
    ///
    /// Records what the counter found. Stock is NOT touched here: a variance raises a
    /// pending InventoryAdjustment for approval, which keeps a single auditable moment
    /// where stock actually changes.
    /// </summary>
    public async Task<CountTaskDto> CompleteCountAsync(
        Guid taskId, CompleteCountRequest request, CancellationToken ct = default)
    {
        if (request.CountedQuantity < 0)
            throw new BusinessRuleViolationException("Counted quantity cannot be negative.");

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var task = await _db.CountTasks
                .Include(t => t.CountPlan)
                .Include(t => t.Item)
                .FirstOrDefaultAsync(
                    t => t.Id == taskId && t.CountPlan.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(CountTask), taskId);

            if (task.Status is CountTaskStatus.Completed or CountTaskStatus.Cancelled
                or CountTaskStatus.Counted or CountTaskStatus.VarianceFound)
            {
                throw new InvalidStateTransitionException(
                    nameof(CountTask), task.Status.ToString(), nameof(CountTaskStatus.Counted));
            }

            // Rule §11.5 - a serial-tracked line is one unit or none.
            if (task.SerialId is not null && request.CountedQuantity > 1)
                throw new BusinessRuleViolationException(
                    "A serial-tracked count cannot exceed one unit.", ruleNumber: 5);

            task.CountedQuantity = request.CountedQuantity;
            task.CountedAt = _clock.UtcNow;
            task.CountedBy = _scope.Username;
            task.Notes = request.Notes;

            if (!task.HasVariance)
            {
                task.TransitionTo(CountTaskStatus.Counted);
                task.TransitionTo(CountTaskStatus.Completed);
            }
            else
            {
                // Variance: raise an adjustment for approval and hold the task open until
                // that adjustment is decided.
                var adjustment = new InventoryAdjustment
                {
                    AccountId = _scope.AccountId,
                    WarehouseId = task.WarehouseId,
                    AdjustmentNumber = await GenerateNumberAsync("ADJ", token),
                    LocationId = task.LocationId,
                    ItemId = task.ItemId,
                    InventoryStatusId = task.InventoryStatusId,
                    LotId = task.LotId,
                    SerialId = task.SerialId,
                    LicensePlateId = task.LicensePlateId,
                    InventoryBalanceId = task.InventoryBalanceId,
                    SystemQuantity = task.SystemQuantity,
                    CountedQuantity = request.CountedQuantity,
                    AdjustmentQuantity = request.CountedQuantity - task.SystemQuantity,
                    Reason = AdjustmentReason.CountVariance,
                    Status = AdjustmentStatus.Pending,
                    CountTaskId = task.Id,
                    RequestedBy = _scope.Username,
                    Notes = request.Notes,
                    CorrelationId = Guid.NewGuid()
                };

                _db.InventoryAdjustments.Add(adjustment);
                await _db.SaveChangesAsync(token);

                task.InventoryAdjustmentId = adjustment.Id;
                task.TransitionTo(CountTaskStatus.VarianceFound);

                _logger.LogInformation(
                    "Count task {TaskId} found a variance of {Variance} for {Sku}; adjustment {AdjustmentNumber} raised by {User}",
                    task.Id, task.Variance, task.Item.Sku, adjustment.AdjustmentNumber, _scope.Username);
            }

            var plan = await _db.CountPlans
                .Include(p => p.Tasks)
                .FirstAsync(p => p.Id == task.CountPlanId, token);

            plan.RecalculateStatus();
            if (plan.Status == CountPlanStatus.Completed) plan.CompletedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(token);

            return await GetTaskAsync(task.Id, token);
        }, ct);
    }

    // =========================================================== adjustments

    /// <summary>Raises a standalone adjustment outside a count, e.g. a damage write-off.</summary>
    public async Task<InventoryAdjustmentDto> CreateAdjustmentAsync(
        CreateAdjustmentRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);
        await _scope.EnsureLocationAsync(request.LocationId, request.WarehouseId, ct);
        await _scope.EnsureItemAsync(request.ItemId, ct);

        if (request.CountedQuantity < 0)
            throw new BusinessRuleViolationException("Counted quantity cannot be negative.");

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var statusId = request.InventoryStatusId ?? await _db.InventoryStatuses
                .Where(s => s.Code == InventoryStatus.Available)
                .Select(s => s.Id).FirstAsync(token);

            var key = new BalanceKey(
                request.WarehouseId, request.LocationId, request.ItemId, statusId,
                request.LotId, request.SerialId, request.LicensePlateId);

            var balance = await _inventory.GetBalanceForUpdateAsync(key, token);
            var systemQuantity = balance?.OnHandQuantity ?? 0m;

            if (request.CountedQuantity == systemQuantity)
                throw new BusinessRuleViolationException(
                    "The proposed quantity matches the current on-hand; there is nothing to adjust.");

            var adjustment = new InventoryAdjustment
            {
                AccountId = _scope.AccountId,
                WarehouseId = request.WarehouseId,
                AdjustmentNumber = await GenerateNumberAsync("ADJ", token),
                LocationId = request.LocationId,
                ItemId = request.ItemId,
                InventoryStatusId = statusId,
                LotId = request.LotId,
                SerialId = request.SerialId,
                LicensePlateId = request.LicensePlateId,
                InventoryBalanceId = balance?.Id,
                SystemQuantity = systemQuantity,
                CountedQuantity = request.CountedQuantity,
                AdjustmentQuantity = request.CountedQuantity - systemQuantity,
                Reason = request.Reason,
                Status = AdjustmentStatus.Pending,
                RequestedBy = _scope.Username,
                Notes = request.Notes,
                CorrelationId = Guid.NewGuid()
            };

            _db.InventoryAdjustments.Add(adjustment);
            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Adjustment {AdjustmentNumber} ({Reason}) raised by {User}: {System} -> {Counted}",
                adjustment.AdjustmentNumber, adjustment.Reason, _scope.Username,
                systemQuantity, request.CountedQuantity);

            return await GetAdjustmentAsync(adjustment.Id, token);
        }, ct);
    }

    /// <summary>
    /// POST /api/inventory-adjustments/{id}/approve (§12).
    ///
    /// This is the only place a count or adjustment changes stock. The balance is row-locked,
    /// its true before value recorded, the change applied, the after value recorded, and one
    /// CountAdjustment ledger row written (rule §11.9) - all in one transaction (§11.12).
    /// </summary>
    public async Task<InventoryAdjustmentDto> ApproveAdjustmentAsync(
        Guid adjustmentId, ApproveAdjustmentRequest? request, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var adjustment = await _db.InventoryAdjustments
                .FirstOrDefaultAsync(
                    a => a.Id == adjustmentId && a.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(InventoryAdjustment), adjustmentId);

            if (adjustment.Status != AdjustmentStatus.Pending)
                throw new InvalidStateTransitionException(
                    nameof(InventoryAdjustment), adjustment.Status.ToString(),
                    nameof(AdjustmentStatus.Approved));

            // Separation of duties: the person who raised an adjustment must not approve
            // their own. Not in the source document (§9 is missing) - see ASSUMPTIONS.md.
            if (!string.IsNullOrEmpty(adjustment.RequestedBy)
                && string.Equals(adjustment.RequestedBy, _scope.Username, StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessRuleViolationException(
                    "An adjustment must be approved by someone other than the person who raised it.");
            }

            var item = await _db.Items.FirstAsync(i => i.Id == adjustment.ItemId, token);

            var key = new BalanceKey(
                adjustment.WarehouseId, adjustment.LocationId, adjustment.ItemId,
                adjustment.InventoryStatusId, adjustment.LotId, adjustment.SerialId,
                adjustment.LicensePlateId);

            // Row-locked read: the true "before" value, not the snapshot from raise time.
            var balance = await _inventory.GetBalanceForUpdateAsync(key, token);
            var before = balance?.OnHandQuantity ?? 0m;

            // Stock can move between raising and approval. The approver signed off on a
            // TARGET quantity, so that target is what is applied, and the drift is flagged.
            adjustment.QuantityBeforeApproval = before;
            adjustment.DriftedBeforeApproval = before != adjustment.SystemQuantity;

            var delta = adjustment.CountedQuantity - before;

            if (delta == 0m)
            {
                throw new BusinessRuleViolationException(
                    $"Stock already matches the counted quantity of {adjustment.CountedQuantity}; " +
                    "there is nothing left to adjust.");
            }

            var correlationId = adjustment.CorrelationId;

            if (delta > 0)
            {
                // Found stock: increase on-hand.
                var target = balance ?? await _inventory.GetOrCreateBalanceForUpdateAsync(
                    key, _clock.UtcNow, token);

                target.AddStock(delta);

                if (target.SerialId is not null && target.OnHandQuantity > 1m)
                    throw new BusinessRuleViolationException(
                        "A serial-tracked balance cannot exceed one unit.", ruleNumber: 5);

                adjustment.InventoryBalanceId = target.Id;
                adjustment.QuantityAfterApproval = target.OnHandQuantity;

                _ledger.RecordNonPhysical(new StockMutation
                {
                    WarehouseId = adjustment.WarehouseId,
                    ItemId = adjustment.ItemId,
                    Quantity = delta,
                    TransactionType = InventoryTransactionType.CountAdjustment,
                    ReferenceType = TransactionReferenceType.InventoryAdjustment,
                    ReferenceId = adjustment.Id,
                    CorrelationId = correlationId,
                    // A positive adjustment brings stock in from outside the system.
                    FromLocationId = null,
                    ToLocationId = adjustment.LocationId,
                    FromInventoryStatusId = null,
                    ToInventoryStatusId = adjustment.InventoryStatusId,
                    LotId = adjustment.LotId,
                    SerialId = adjustment.SerialId,
                    LicensePlateId = adjustment.LicensePlateId,
                    Notes = $"{adjustment.AdjustmentNumber} ({adjustment.Reason}): {before} -> {adjustment.CountedQuantity}"
                });
            }
            else
            {
                // Missing stock: decrease on-hand. Reserved and held quantities are
                // protected, so a write-off can never consume another order's stock.
                if (balance is null)
                    throw new NotFoundException(
                        "There is no stock at this location to reduce.");

                balance.RemoveStock(-delta, item.Sku);
                adjustment.QuantityAfterApproval = balance.OnHandQuantity;

                _ledger.RecordNonPhysical(new StockMutation
                {
                    WarehouseId = adjustment.WarehouseId,
                    ItemId = adjustment.ItemId,
                    Quantity = -delta,
                    TransactionType = InventoryTransactionType.CountAdjustment,
                    ReferenceType = TransactionReferenceType.InventoryAdjustment,
                    ReferenceId = adjustment.Id,
                    CorrelationId = correlationId,
                    // A negative adjustment removes stock from the system entirely.
                    FromLocationId = adjustment.LocationId,
                    ToLocationId = null,
                    FromInventoryStatusId = adjustment.InventoryStatusId,
                    ToInventoryStatusId = null,
                    LotId = adjustment.LotId,
                    SerialId = adjustment.SerialId,
                    LicensePlateId = adjustment.LicensePlateId,
                    Notes = $"{adjustment.AdjustmentNumber} ({adjustment.Reason}): {before} -> {adjustment.CountedQuantity}"
                });

                await _inventory.RemoveEmptyBalanceAsync(balance, token);
            }

            adjustment.AdjustmentQuantity = delta;
            adjustment.ApprovedBy = _scope.Username;
            adjustment.ApprovedAt = _clock.UtcNow;
            adjustment.TransitionTo(AdjustmentStatus.Approved);

            if (!string.IsNullOrWhiteSpace(request?.Notes))
                adjustment.Notes = string.IsNullOrWhiteSpace(adjustment.Notes)
                    ? request.Notes
                    : $"{adjustment.Notes} | Approval: {request.Notes}";

            await CloseCountTaskAsync(adjustment, token);

            await _db.SaveChangesAsync(token);

            // Link the ledger row back to the adjustment for the audit trail.
            var txId = await _db.InventoryTransactions
                .Where(t => t.CorrelationId == correlationId
                            && t.ReferenceId == adjustment.Id)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(token);

            adjustment.InventoryTransactionId = txId;
            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Adjustment {AdjustmentNumber} APPROVED by {Approver} (raised by {Requester}): {Before} -> {After}",
                adjustment.AdjustmentNumber, _scope.Username, adjustment.RequestedBy,
                adjustment.QuantityBeforeApproval, adjustment.QuantityAfterApproval);

            return await GetAdjustmentAsync(adjustment.Id, token);
        }, ct);
    }

    /// <summary>Rejects an adjustment. No stock moves; the decision and reason are recorded.</summary>
    public async Task<InventoryAdjustmentDto> RejectAdjustmentAsync(
        Guid adjustmentId, RejectAdjustmentRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RejectionReason))
            throw new BusinessRuleViolationException("A rejection reason is required.");

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var adjustment = await _db.InventoryAdjustments
                .FirstOrDefaultAsync(
                    a => a.Id == adjustmentId && a.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(InventoryAdjustment), adjustmentId);

            if (adjustment.Status != AdjustmentStatus.Pending)
                throw new InvalidStateTransitionException(
                    nameof(InventoryAdjustment), adjustment.Status.ToString(),
                    nameof(AdjustmentStatus.Rejected));

            adjustment.RejectionReason = request.RejectionReason;
            adjustment.ApprovedBy = _scope.Username;
            adjustment.ApprovedAt = _clock.UtcNow;
            adjustment.TransitionTo(AdjustmentStatus.Rejected);

            await CloseCountTaskAsync(adjustment, token);
            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Adjustment {AdjustmentNumber} REJECTED by {User}: {Reason}",
                adjustment.AdjustmentNumber, _scope.Username, request.RejectionReason);

            return await GetAdjustmentAsync(adjustment.Id, token);
        }, ct);
    }

    /// <summary>Closes the originating count task once its adjustment has been decided.</summary>
    private async Task CloseCountTaskAsync(InventoryAdjustment adjustment, CancellationToken ct)
    {
        if (adjustment.CountTaskId is not Guid taskId) return;

        var task = await _db.CountTasks
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task is null || task.Status != CountTaskStatus.VarianceFound) return;

        task.TransitionTo(CountTaskStatus.Completed);

        var plan = await _db.CountPlans
            .Include(p => p.Tasks)
            .FirstAsync(p => p.Id == task.CountPlanId, ct);

        plan.RecalculateStatus();
        if (plan.Status == CountPlanStatus.Completed) plan.CompletedAt = _clock.UtcNow;
    }

    /// <summary>
    /// The complete audit trail for one adjustment, assembled from the plan, task,
    /// adjustment and ledger records.
    /// </summary>
    public async Task<AdjustmentAuditDto> GetAuditTrailAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        var a = await _db.InventoryAdjustments
            .Include(x => x.Item)
            .Include(x => x.Location)
            .Include(x => x.Lot)
            .FirstOrDefaultAsync(x => x.Id == adjustmentId && x.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(InventoryAdjustment), adjustmentId);

        var timeline = new List<AuditEventDto>();

        CountTask? task = null;
        CountPlan? plan = null;

        if (a.CountTaskId is Guid taskId)
        {
            task = await _db.CountTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);

            if (task is not null)
            {
                plan = await _db.CountPlans.FirstOrDefaultAsync(p => p.Id == task.CountPlanId, ct);

                if (plan is not null)
                {
                    timeline.Add(new AuditEventDto(
                        "CountPlanCreated", plan.CreatedBy, plan.CreatedAt,
                        $"Plan {plan.PlanNumber} ({plan.CountType}, {plan.SelectionMode})"));

                    if (plan.ReleasedAt.HasValue)
                        timeline.Add(new AuditEventDto(
                            "CountPlanReleased", plan.UpdatedBy ?? plan.CreatedBy, plan.ReleasedAt,
                            "Count tasks generated from current stock"));
                }

                timeline.Add(new AuditEventDto(
                    "CountTaskCreated", plan?.CreatedBy, task.CreatedAt,
                    $"System quantity snapshot: {task.SystemQuantity}"));

                if (task.CountedAt.HasValue)
                    timeline.Add(new AuditEventDto(
                        "Counted", task.CountedBy, task.CountedAt,
                        $"Counted {task.CountedQuantity} against a system quantity of " +
                        $"{task.SystemQuantity} (variance {task.Variance})"));
            }
        }

        timeline.Add(new AuditEventDto(
            "AdjustmentRaised", a.RequestedBy, a.CreatedAt,
            $"{a.Reason}: proposes {a.SystemQuantity} -> {a.CountedQuantity}"));

        if (a.Status == AdjustmentStatus.Approved)
        {
            timeline.Add(new AuditEventDto(
                "Approved", a.ApprovedBy, a.ApprovedAt,
                $"Applied: on-hand {a.QuantityBeforeApproval} -> {a.QuantityAfterApproval} " +
                $"(delta {a.AdjustmentQuantity})" +
                (a.DriftedBeforeApproval
                    ? $"; NOTE stock had drifted from the {a.SystemQuantity} seen at raise time"
                    : string.Empty)));
        }
        else if (a.Status == AdjustmentStatus.Rejected)
        {
            timeline.Add(new AuditEventDto(
                "Rejected", a.ApprovedBy, a.ApprovedAt,
                $"No stock change. Reason: {a.RejectionReason}"));
        }

        return new AdjustmentAuditDto(
            a.Id, a.AdjustmentNumber, a.Item.Sku, a.Location.Code, a.Lot?.LotNumber,
            a.Reason, a.Status,
            timeline.OrderBy(e => e.At ?? DateTimeOffset.MaxValue).ToList(),
            a.SystemQuantity, a.CountedQuantity,
            a.QuantityBeforeApproval, a.QuantityAfterApproval,
            a.AdjustmentQuantity, a.DriftedBeforeApproval,
            a.InventoryTransactionId, a.CorrelationId);
    }

    // =================================================================== reads

    public async Task<PagedResult<CountPlanSummaryDto>> ListPlansAsync(
        CountPlanQuery query, CancellationToken ct = default)
    {
        var q = _db.CountPlans.Where(p => p.AccountId == _scope.AccountId);

        if (query.WarehouseId.HasValue) q = q.Where(p => p.WarehouseId == query.WarehouseId.Value);
        if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);
        if (query.CountType.HasValue) q = q.Where(p => p.CountType == query.CountType.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(p => new CountPlanSummaryDto(
                p.Id, p.PlanNumber, p.Name, p.WarehouseId,
                p.CountType, p.Status, p.ScheduledDate, p.CreatedBy, p.CreatedAt,
                p.Tasks.Count,
                p.Tasks.Count(t => t.CountedQuantity != null),
                p.Tasks.Count(t => t.InventoryAdjustmentId != null)))
            .ToListAsync(ct);

        return new PagedResult<CountPlanSummaryDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<CountPlanDto> GetPlanAsync(Guid id, CancellationToken ct = default)
    {
        var plan = await _db.CountPlans
            .Include(p => p.Zone)
            .Include(p => p.Item)
            .FirstOrDefaultAsync(p => p.Id == id && p.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(CountPlan), id);

        var tasks = await ScopedTasks()
            .Where(t => t.CountPlanId == id)
            .OrderBy(t => t.CreatedAt)
            .Select(TaskProjection)
            .ToListAsync(ct);

        return new CountPlanDto(
            plan.Id, plan.WarehouseId, plan.PlanNumber, plan.Name,
            plan.CountType, plan.SelectionMode,
            plan.ZoneId, plan.Zone?.Code,
            plan.ItemId, plan.Item?.Sku,
            plan.Status, plan.ScheduledDate, plan.ReleasedAt, plan.CompletedAt,
            plan.BlockAllocationDuringCount, plan.Notes,
            plan.CreatedBy, plan.CreatedAt, plan.UpdatedBy, plan.UpdatedAt,
            tasks.Count,
            tasks.Count(t => t.CountedQuantity is not null),
            tasks.Count(t => t.InventoryAdjustmentId is not null),
            tasks);
    }

    public async Task<CountTaskDto> GetTaskAsync(Guid id, CancellationToken ct = default)
        => await ScopedTasks()
               .Where(t => t.Id == id)
               .Select(TaskProjection)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(nameof(CountTask), id);

    public async Task<PagedResult<CountTaskDto>> ListTasksAsync(
        CountTaskQuery query, CancellationToken ct = default)
    {
        var q = ScopedTasks();

        if (query.CountPlanId.HasValue) q = q.Where(t => t.CountPlanId == query.CountPlanId.Value);
        if (query.WarehouseId.HasValue) q = q.Where(t => t.WarehouseId == query.WarehouseId.Value);
        if (query.Status.HasValue) q = q.Where(t => t.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.AssignedTo)) q = q.Where(t => t.AssignedTo == query.AssignedTo);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(t => t.Status).ThenBy(t => t.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(TaskProjection)
            .ToListAsync(ct);

        return new PagedResult<CountTaskDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<PagedResult<InventoryAdjustmentDto>> ListAdjustmentsAsync(
        AdjustmentQuery query, CancellationToken ct = default)
    {
        var q = _db.InventoryAdjustments.Where(a => a.AccountId == _scope.AccountId);

        if (query.WarehouseId.HasValue) q = q.Where(a => a.WarehouseId == query.WarehouseId.Value);
        if (query.Status.HasValue) q = q.Where(a => a.Status == query.Status.Value);
        if (query.Reason.HasValue) q = q.Where(a => a.Reason == query.Reason.Value);
        if (query.ItemId.HasValue) q = q.Where(a => a.ItemId == query.ItemId.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(AdjustmentProjection)
            .ToListAsync(ct);

        return new PagedResult<InventoryAdjustmentDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<InventoryAdjustmentDto> GetAdjustmentAsync(Guid id, CancellationToken ct = default)
        => await _db.InventoryAdjustments
               .Where(a => a.Id == id && a.AccountId == _scope.AccountId)
               .Select(AdjustmentProjection)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(nameof(InventoryAdjustment), id);

    /// <summary>
    /// Account-scoped tasks as ENTITIES. Filtering must happen before the projection:
    /// EF cannot translate a Where applied to an already-projected DTO.
    /// </summary>
    private IQueryable<CountTask> ScopedTasks()
        => _db.CountTasks.Where(t => t.CountPlan.AccountId == _scope.AccountId);

    private async Task<string> GenerateNumberAsync(string prefix, CancellationToken ct)
    {
        var stem = $"{prefix}-{_clock.UtcNow:yyyyMMdd}-";

        var last = prefix == "CP"
            ? await _db.CountPlans
                .Where(p => p.AccountId == _scope.AccountId && p.PlanNumber.StartsWith(stem))
                .OrderByDescending(p => p.PlanNumber).Select(p => p.PlanNumber).FirstOrDefaultAsync(ct)
            : await _db.InventoryAdjustments
                .Where(a => a.AccountId == _scope.AccountId && a.AdjustmentNumber.StartsWith(stem))
                .OrderByDescending(a => a.AdjustmentNumber).Select(a => a.AdjustmentNumber).FirstOrDefaultAsync(ct);

        var next = last is null ? 1 : int.Parse(last[stem.Length..]) + 1;
        return stem + next.ToString("D4");
    }

    /// <summary>Expression-shaped so EF can translate it inside Select.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<CountTask, CountTaskDto>> TaskProjection =
        t => new CountTaskDto(
            t.Id, t.CountPlanId, t.WarehouseId,
            t.LocationId, t.Location.Code,
            t.ItemId, t.Item.Sku, t.Item.Name,
            t.InventoryStatusId, t.InventoryStatus.Code,
            t.LotId, t.Lot != null ? t.Lot.LotNumber : null,
            t.SerialId, t.Serial != null ? t.Serial.Serial : null,
            t.LicensePlateId, t.LicensePlate != null ? t.LicensePlate.Code : null,
            t.InventoryBalanceId,
            t.SystemQuantity, t.CountedQuantity,
            t.CountedQuantity != null ? t.CountedQuantity - t.SystemQuantity : null,
            t.CountedQuantity != null && t.CountedQuantity != t.SystemQuantity,
            t.Status,
            t.AssignedTo, t.CountedBy, t.CountedAt,
            t.CreatedBy, t.CreatedAt,
            t.InventoryAdjustmentId, t.Notes);

    private static readonly System.Linq.Expressions.Expression<Func<InventoryAdjustment, InventoryAdjustmentDto>>
        AdjustmentProjection = a => new InventoryAdjustmentDto(
            a.Id, a.AdjustmentNumber, a.WarehouseId,
            a.LocationId, a.Location.Code,
            a.ItemId, a.Item.Sku, a.Item.Name,
            a.InventoryStatusId, a.InventoryStatus.Code,
            a.LotId, a.Lot != null ? a.Lot.LotNumber : null,
            a.SerialId, a.Serial != null ? a.Serial.Serial : null,
            a.LicensePlateId, a.InventoryBalanceId,
            a.SystemQuantity, a.CountedQuantity, a.AdjustmentQuantity,
            a.QuantityBeforeApproval, a.QuantityAfterApproval, a.DriftedBeforeApproval,
            a.Reason, a.Status, a.CountTaskId, a.InventoryTransactionId,
            a.RequestedBy, a.CreatedAt, a.ApprovedBy, a.ApprovedAt,
            a.RejectionReason, a.Notes, a.CorrelationId);
}
