namespace IMS.Domain.Enums;

/// <summary>Doc §3.3 - logical/physical area inside a warehouse.</summary>
public enum ZoneType
{
    Receiving = 1,
    QualityControl = 2,
    ReserveStorage = 3,
    Picking = 4,
    Packing = 5,
    ShippingStaging = 6,
    Damaged = 7,
    Returns = 8,
    ColdStorage = 9,
    HazardousMaterial = 10
}

/// <summary>Doc §3.5 - shared rule-set family for similar locations.</summary>
public enum LocationType
{
    SmallBin = 1,
    StandardShelf = 2,
    PalletRack = 3,
    FloorStorage = 4,
    ColdStorage = 5,
    DangerousGoods = 6,
    PickFace = 7,
    ReserveStorage = 8
}

/// <summary>Doc §4.2 - storage type of a dynamic item attribute value.</summary>
public enum AttributeDataType
{
    Text = 1,
    Number = 2,
    Boolean = 3,
    Date = 4
}

/// <summary>Doc §4.4 - ItemBarcode.BarcodeType. Common retail/logistics symbologies.</summary>
public enum BarcodeType
{
    Ean13 = 1,
    Ean8 = 2,
    Upca = 3,
    Upce = 4,
    Code128 = 5,
    Code39 = 6,
    Itf14 = 7,
    Gs1128 = 8,
    QrCode = 9,
    DataMatrix = 10
}
