using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Invoices;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;

public interface IInvoiceService
{
    Task<InvoiceDetailResponse> GetInvoiceByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long invoiceId,
        CancellationToken cancellationToken = default);
}
