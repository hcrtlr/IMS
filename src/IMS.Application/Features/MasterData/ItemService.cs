using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.MasterData;

/// <summary>
/// Doc §4 - item master, dynamic attributes (§4.2), UOM conversions (§4.3) and
/// barcodes (§4.4).
/// </summary>
public class ItemService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public ItemService(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    private IQueryable<ItemMaster> Scoped()
        => _db.Items.Where(i => i.AccountId == _scope.AccountId);

    public async Task<PagedResult<ItemSummaryDto>> ListAsync(
        PagedQuery query, bool? isActive = null, Guid? categoryId = null, CancellationToken ct = default)
    {
        var q = Scoped();

        if (isActive.HasValue) q = q.Where(i => i.IsActive == isActive.Value);
        if (categoryId.HasValue) q = q.Where(i => i.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(i => i.Sku.ToLower().Contains(term) || i.Name.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(i => i.Sku)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(i => new ItemSummaryDto(
                i.Id, i.Sku, i.Name,
                i.Category != null ? i.Category.Name : null,
                i.BaseUom.Code,
                i.IsLotTracked, i.IsSerialTracked, i.IsExpirationTracked, i.IsActive))
            .ToListAsync(ct);

        return new PagedResult<ItemSummaryDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<ItemDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await Scoped()
            .Include(i => i.Category)
            .Include(i => i.BaseUom)
            .Include(i => i.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .Include(i => i.ItemUoms).ThenInclude(u => u.Uom)
            .Include(i => i.Barcodes).ThenInclude(bc => bc.Uom)
            .FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), id);

        return ToDto(item);
    }

    public async Task<ItemDto> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        var item = await Scoped()
            .Include(i => i.Category)
            .Include(i => i.BaseUom)
            .Include(i => i.AttributeValues).ThenInclude(v => v.AttributeDefinition)
            .Include(i => i.ItemUoms).ThenInclude(u => u.Uom)
            .Include(i => i.Barcodes).ThenInclude(bc => bc.Uom)
            .FirstOrDefaultAsync(i => i.Sku == sku, ct)
            ?? throw new NotFoundException($"Item with SKU '{sku}' was not found.");

        return ToDto(item);
    }

    public async Task<ItemDto> CreateAsync(CreateItemRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        // Doc §4.1: SKU is unique within an account.
        if (await _db.Items.AnyAsync(i => i.AccountId == accountId && i.Sku == request.Sku, ct))
            throw new DuplicateEntityException($"SKU '{request.Sku}' already exists in this account.");

        await ValidateItemInputAsync(
            request.BaseUomId, request.CategoryId,
            request.DefaultPutawayZoneId, request.DefaultPickZoneId,
            request.IsExpirationTracked, request.ShelfLifeDays,
            request.IsSerialTracked, request.IsLotTracked,
            request.IsTemperatureControlled,
            request.MinimumStorageTemperature, request.MaximumStorageTemperature, ct);

        var item = new ItemMaster
        {
            AccountId = accountId,
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            CategoryId = request.CategoryId,
            BaseUomId = request.BaseUomId,
            Weight = request.Weight,
            Length = request.Length,
            Width = request.Width,
            Height = request.Height,
            Volume = request.Volume ?? ComputeVolume(request.Length, request.Width, request.Height),
            IsLotTracked = request.IsLotTracked,
            IsSerialTracked = request.IsSerialTracked,
            IsExpirationTracked = request.IsExpirationTracked,
            ShelfLifeDays = request.ShelfLifeDays,
            IsFragile = request.IsFragile,
            IsHazardous = request.IsHazardous,
            IsTemperatureControlled = request.IsTemperatureControlled,
            MinimumStorageTemperature = request.MinimumStorageTemperature,
            MaximumStorageTemperature = request.MaximumStorageTemperature,
            StackableQuantity = request.StackableQuantity,
            DefaultPutawayZoneId = request.DefaultPutawayZoneId,
            DefaultPickZoneId = request.DefaultPickZoneId,
            IsActive = true
        };

        _db.Items.Add(item);

        // Every item gets its base UOM as a 1:1 conversion so quantity maths always
        // has a row to resolve against.
        _db.ItemUoms.Add(new ItemUom
        {
            ItemId = item.Id,
            UomId = request.BaseUomId,
            ConversionQuantity = 1m,
            IsReceivingUom = true,
            IsPickingUom = true,
            IsShippingUom = true,
            IsActive = true
        });

        await _db.SaveChangesAsync(ct);
        return await GetAsync(item.Id, ct);
    }

    public async Task<ItemDto> UpdateAsync(Guid id, UpdateItemRequest request, CancellationToken ct = default)
    {
        var item = await Scoped().FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), id);

        await ValidateItemInputAsync(
            request.BaseUomId, request.CategoryId,
            request.DefaultPutawayZoneId, request.DefaultPickZoneId,
            request.IsExpirationTracked, request.ShelfLifeDays,
            request.IsSerialTracked, request.IsLotTracked,
            request.IsTemperatureControlled,
            request.MinimumStorageTemperature, request.MaximumStorageTemperature, ct);

        // Changing tracking flags while stock exists would orphan lot/serial records and
        // invalidate rules §11.5 and §11.6 for what is already on hand.
        var trackingChanged =
            item.IsLotTracked != request.IsLotTracked ||
            item.IsSerialTracked != request.IsSerialTracked ||
            item.IsExpirationTracked != request.IsExpirationTracked;

        if (trackingChanged)
        {
            var hasStock = await _db.InventoryBalances.AnyAsync(b => b.ItemId == id && b.OnHandQuantity > 0, ct);
            if (hasStock)
                throw new BusinessRuleViolationException(
                    "Lot, serial and expiration tracking cannot be changed while the item has stock on hand.");
        }

        if (item.BaseUomId != request.BaseUomId)
        {
            var hasStock = await _db.InventoryBalances.AnyAsync(b => b.ItemId == id && b.OnHandQuantity > 0, ct);
            if (hasStock)
                throw new BusinessRuleViolationException(
                    "The base unit of measure cannot be changed while the item has stock on hand.");
        }

        item.Name = request.Name;
        item.Description = request.Description;
        item.CategoryId = request.CategoryId;
        item.BaseUomId = request.BaseUomId;
        item.Weight = request.Weight;
        item.Length = request.Length;
        item.Width = request.Width;
        item.Height = request.Height;
        item.Volume = request.Volume ?? ComputeVolume(request.Length, request.Width, request.Height);
        item.IsLotTracked = request.IsLotTracked;
        item.IsSerialTracked = request.IsSerialTracked;
        item.IsExpirationTracked = request.IsExpirationTracked;
        item.ShelfLifeDays = request.ShelfLifeDays;
        item.IsFragile = request.IsFragile;
        item.IsHazardous = request.IsHazardous;
        item.IsTemperatureControlled = request.IsTemperatureControlled;
        item.MinimumStorageTemperature = request.MinimumStorageTemperature;
        item.MaximumStorageTemperature = request.MaximumStorageTemperature;
        item.StackableQuantity = request.StackableQuantity;
        item.DefaultPutawayZoneId = request.DefaultPutawayZoneId;
        item.DefaultPickZoneId = request.DefaultPickZoneId;
        item.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // --- Dynamic attributes (doc §4.2) ---------------------------------------

    /// <summary>
    /// Sets or replaces one attribute value. The column written is chosen by the
    /// definition's DataType, so a Number attribute can never be stored as text.
    /// </summary>
    public async Task<ItemDto> SetAttributeAsync(
        Guid itemId, SetItemAttributeRequest request, CancellationToken ct = default)
    {
        var item = await Scoped().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        var definition = await _db.AttributeDefinitions
            .FirstOrDefaultAsync(a => a.Id == request.AttributeDefinitionId
                                      && a.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(AttributeDefinition), request.AttributeDefinitionId);

        var value = await _db.ItemAttributeValues
            .FirstOrDefaultAsync(v => v.ItemId == itemId
                                      && v.AttributeDefinitionId == definition.Id, ct);

        if (value is null)
        {
            value = new ItemAttributeValue
            {
                ItemId = itemId,
                AttributeDefinitionId = definition.Id
            };
            _db.ItemAttributeValues.Add(value);
        }

        // Clear every column, then populate only the one matching the declared type.
        value.TextValue = null;
        value.NumberValue = null;
        value.BooleanValue = null;
        value.DateValue = null;

        switch (definition.DataType)
        {
            case AttributeDataType.Text:
                value.TextValue = request.TextValue
                    ?? throw new BusinessRuleViolationException(
                        $"Attribute '{definition.Code}' is Text; TextValue is required.");
                break;

            case AttributeDataType.Number:
                value.NumberValue = request.NumberValue
                    ?? throw new BusinessRuleViolationException(
                        $"Attribute '{definition.Code}' is Number; NumberValue is required.");
                break;

            case AttributeDataType.Boolean:
                value.BooleanValue = request.BooleanValue
                    ?? throw new BusinessRuleViolationException(
                        $"Attribute '{definition.Code}' is Boolean; BooleanValue is required.");
                break;

            case AttributeDataType.Date:
                value.DateValue = request.DateValue
                    ?? throw new BusinessRuleViolationException(
                        $"Attribute '{definition.Code}' is Date; DateValue is required.");
                break;
        }

        await _db.SaveChangesAsync(ct);
        return await GetAsync(itemId, ct);
    }

    public async Task RemoveAttributeAsync(Guid itemId, Guid attributeDefinitionId, CancellationToken ct = default)
    {
        var item = await Scoped().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        var definition = await _db.AttributeDefinitions
            .FirstOrDefaultAsync(a => a.Id == attributeDefinitionId, ct);

        if (definition?.IsRequired == true)
            throw new BusinessRuleViolationException(
                $"Attribute '{definition.Code}' is required and cannot be removed.");

        var value = await _db.ItemAttributeValues
            .FirstOrDefaultAsync(v => v.ItemId == itemId && v.AttributeDefinitionId == attributeDefinitionId, ct);

        if (value is null) return;

        _db.ItemAttributeValues.Remove(value);
        await _db.SaveChangesAsync(ct);
    }

    // --- UOM conversions (doc §4.3) ------------------------------------------

    public async Task<ItemDto> AddUomAsync(Guid itemId, CreateItemUomRequest request, CancellationToken ct = default)
    {
        var item = await Scoped().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        if (request.ConversionQuantity <= 0)
            throw new BusinessRuleViolationException("ConversionQuantity must be greater than zero.");

        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.UomId, ct))
            throw new NotFoundException(nameof(UnitOfMeasure), request.UomId);

        if (await _db.ItemUoms.AnyAsync(u => u.ItemId == itemId && u.UomId == request.UomId, ct))
            throw new DuplicateEntityException("This item already has a conversion for that unit of measure.");

        _db.ItemUoms.Add(new ItemUom
        {
            ItemId = itemId,
            UomId = request.UomId,
            ConversionQuantity = request.ConversionQuantity,
            Barcode = request.Barcode,
            Length = request.Length,
            Width = request.Width,
            Height = request.Height,
            Weight = request.Weight,
            IsReceivingUom = request.IsReceivingUom,
            IsPickingUom = request.IsPickingUom,
            IsShippingUom = request.IsShippingUom,
            IsActive = true
        });

        await _db.SaveChangesAsync(ct);
        return await GetAsync(itemId, ct);
    }

    public async Task RemoveUomAsync(Guid itemId, Guid itemUomId, CancellationToken ct = default)
    {
        var item = await Scoped().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        var itemUom = await _db.ItemUoms.FirstOrDefaultAsync(u => u.Id == itemUomId && u.ItemId == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemUom), itemUomId);

        if (itemUom.UomId == item.BaseUomId)
            throw new BusinessRuleViolationException("The base unit of measure conversion cannot be removed.");

        _db.ItemUoms.Remove(itemUom);
        await _db.SaveChangesAsync(ct);
    }

    // --- Barcodes (doc §4.4) -------------------------------------------------

    public async Task<ItemDto> AddBarcodeAsync(
        Guid itemId, CreateItemBarcodeRequest request, CancellationToken ct = default)
    {
        var item = await Scoped().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        if (await _db.ItemBarcodes.AnyAsync(bc => bc.Barcode == request.Barcode, ct))
            throw new DuplicateEntityException($"Barcode '{request.Barcode}' is already assigned.");

        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == request.UomId, ct))
            throw new NotFoundException(nameof(UnitOfMeasure), request.UomId);

        if (request.IsPrimary)
        {
            // Doc §4.4 allows many barcodes but only one may be primary.
            var currentPrimaries = await _db.ItemBarcodes
                .Where(bc => bc.ItemId == itemId && bc.IsPrimary)
                .ToListAsync(ct);

            foreach (var existing in currentPrimaries) existing.IsPrimary = false;
        }

        _db.ItemBarcodes.Add(new ItemBarcode
        {
            ItemId = itemId,
            UomId = request.UomId,
            Barcode = request.Barcode,
            BarcodeType = request.BarcodeType,
            IsPrimary = request.IsPrimary,
            IsActive = true
        });

        await _db.SaveChangesAsync(ct);
        return await GetAsync(itemId, ct);
    }

    public async Task RemoveBarcodeAsync(Guid itemId, Guid barcodeId, CancellationToken ct = default)
    {
        _ = await Scoped().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        var barcode = await _db.ItemBarcodes
            .FirstOrDefaultAsync(bc => bc.Id == barcodeId && bc.ItemId == itemId, ct)
            ?? throw new NotFoundException(nameof(ItemBarcode), barcodeId);

        _db.ItemBarcodes.Remove(barcode);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Resolves a scanned barcode to its item and UOM.</summary>
    public async Task<ItemDto> FindByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        var itemId = await _db.ItemBarcodes
            .Where(bc => bc.Barcode == barcode && bc.Item.AccountId == _scope.AccountId)
            .Select(bc => (Guid?)bc.ItemId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"No item found for barcode '{barcode}'.");

        return await GetAsync(itemId, ct);
    }

    // --- Validation ----------------------------------------------------------

    private async Task ValidateItemInputAsync(
        Guid baseUomId, Guid? categoryId, Guid? putawayZoneId, Guid? pickZoneId,
        bool isExpirationTracked, int? shelfLifeDays,
        bool isSerialTracked, bool isLotTracked,
        bool isTemperatureControlled, decimal? minTemp, decimal? maxTemp,
        CancellationToken ct)
    {
        if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == baseUomId, ct))
            throw new NotFoundException(nameof(UnitOfMeasure), baseUomId);

        if (categoryId.HasValue &&
            !await _db.ItemCategories.AnyAsync(c => c.Id == categoryId && c.AccountId == _scope.AccountId, ct))
            throw new NotFoundException(nameof(ItemCategory), categoryId.Value);

        if (putawayZoneId.HasValue &&
            !await _db.Zones.AnyAsync(z => z.Id == putawayZoneId && z.Warehouse.AccountId == _scope.AccountId, ct))
            throw new NotFoundException($"Default putaway zone '{putawayZoneId}' was not found.");

        if (pickZoneId.HasValue &&
            !await _db.Zones.AnyAsync(z => z.Id == pickZoneId && z.Warehouse.AccountId == _scope.AccountId, ct))
            throw new NotFoundException($"Default pick zone '{pickZoneId}' was not found.");

        // ShelfLifeDays is optional: it only lets the system DERIVE an expiration date when
        // a receipt does not supply one. Doc §11.6 ("expiration date is mandatory for
        // expiration-tracked items") is about the date itself, so it is enforced at receipt
        // time in ReceivingService, where the date is actually captured. Here we only
        // reject a nonsensical value.
        if (shelfLifeDays is <= 0)
            throw new BusinessRuleViolationException(
                "ShelfLifeDays must be greater than zero when supplied.", ruleNumber: 6);

        // Expiration is a lot-level attribute (doc §5.3), so tracking one implies the other.
        if (isExpirationTracked && !isLotTracked && !isSerialTracked)
            throw new BusinessRuleViolationException(
                "Expiration tracking requires lot tracking (expiration dates are held on the lot).",
                ruleNumber: 6);

        if (isTemperatureControlled && minTemp is null && maxTemp is null)
            throw new BusinessRuleViolationException(
                "A temperature-controlled item must declare a minimum or maximum storage temperature.",
                ruleNumber: 8);

        if (minTemp.HasValue && maxTemp.HasValue && minTemp > maxTemp)
            throw new BusinessRuleViolationException(
                "MinimumStorageTemperature cannot be greater than MaximumStorageTemperature.");
    }

    private static decimal? ComputeVolume(decimal? length, decimal? width, decimal? height)
        => length.HasValue && width.HasValue && height.HasValue
            ? length.Value * width.Value * height.Value
            : null;

    private static ItemDto ToDto(ItemMaster i) => new(
        i.Id, i.AccountId, i.Sku, i.Name, i.Description,
        i.CategoryId, i.Category?.Name,
        i.BaseUomId, i.BaseUom?.Code ?? string.Empty,
        i.Weight, i.Length, i.Width, i.Height, i.Volume,
        i.IsLotTracked, i.IsSerialTracked, i.IsExpirationTracked, i.ShelfLifeDays,
        i.IsFragile, i.IsHazardous, i.IsTemperatureControlled,
        i.MinimumStorageTemperature, i.MaximumStorageTemperature,
        i.StackableQuantity, i.DefaultPutawayZoneId, i.DefaultPickZoneId,
        i.IsActive,
        i.AttributeValues.Select(v => new ItemAttributeValueDto(
            v.Id, v.AttributeDefinitionId,
            v.AttributeDefinition?.Code ?? string.Empty,
            v.AttributeDefinition?.Name ?? string.Empty,
            v.AttributeDefinition?.DataType ?? AttributeDataType.Text,
            v.TextValue, v.NumberValue, v.BooleanValue, v.DateValue)).ToList(),
        i.ItemUoms.Select(u => new ItemUomDto(
            u.Id, u.UomId, u.Uom?.Code ?? string.Empty, u.Uom?.Name ?? string.Empty,
            u.ConversionQuantity, u.Barcode, u.Length, u.Width, u.Height, u.Weight,
            u.IsReceivingUom, u.IsPickingUom, u.IsShippingUom, u.IsActive)).ToList(),
        i.Barcodes.Select(bc => new ItemBarcodeDto(
            bc.Id, bc.UomId, bc.Uom?.Code ?? string.Empty,
            bc.Barcode, bc.BarcodeType, bc.IsPrimary, bc.IsActive)).ToList());
}
