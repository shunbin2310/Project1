using Project1.Api.DTOs.Suppliers;

namespace Project1.Api.Services.Suppliers;

public enum SupplierSaveStatus
{
    Success,
    NotFound
}

public sealed record SupplierSaveResult(
    SupplierSaveStatus Status,
    SupplierResponse? Supplier = null);
