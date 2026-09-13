using Project1.Api.DTOs.Quotations;
using Project1.Api.Entities;

namespace Project1.Api.Services.Quotations;

public interface IQuotationService
{
    Task<IReadOnlyList<QuotationResponse>> GetAllAsync(
        int? purchaseRequestId,
        QuotationStatus? status,
        CancellationToken cancellationToken);

    Task<QuotationResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<QuotationComparisonResponse?> GetComparisonAsync(
        int purchaseRequestId,
        CancellationToken cancellationToken);

    Task<QuotationOperationResult> CreateAsync(
        CreateQuotationRequest request,
        CancellationToken cancellationToken);

    Task<QuotationOperationResult> UpdateAsync(
        int id,
        UpdateQuotationRequest request,
        CancellationToken cancellationToken);

    Task<QuotationOperationResult> SubmitAsync(int id, CancellationToken cancellationToken);

    Task<QuotationOperationResult> SelectAsync(int id, CancellationToken cancellationToken);

    Task<QuotationOperationResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
