using Project1.Api.DTOs.UnitsOfMeasure;

namespace Project1.Api.Services.UnitsOfMeasure;

public enum UnitOfMeasureSaveStatus
{
    Success,
    NotFound,
    DuplicateCode
}

public sealed record UnitOfMeasureSaveResult(
    UnitOfMeasureSaveStatus Status,
    UnitOfMeasureResponse? UnitOfMeasure = null);
