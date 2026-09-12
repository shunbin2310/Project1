namespace Project1.Api.DTOs.SupplierProducts;

public sealed record SupplierProductResponse(
    int Id,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitOfMeasureCode,
    string UnitOfMeasureName,
    decimal ProductDefaultUnitPrice,
    bool IsPreferred,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
