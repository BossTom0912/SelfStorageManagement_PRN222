using SelfStorageManagementSystem.BusinessLogic.Common.Constants;
using SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.Invoices;
using SelfStorageManagementSystem.BusinessLogic.Exceptions;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Repositories.Interfaces;

namespace SelfStorageManagementSystem.BusinessLogic.Services.Implementations;

public class InvoiceService : IInvoiceService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IFacilityScopeService _facilityScopeService;

    public InvoiceService(
        IPaymentRepository paymentRepository,
        IFacilityScopeService facilityScopeService)
    {
        _paymentRepository = paymentRepository ?? throw new ArgumentNullException(nameof(paymentRepository));
        _facilityScopeService = facilityScopeService ?? throw new ArgumentNullException(nameof(facilityScopeService));
    }

    public async Task<InvoiceDetailResponse> GetInvoiceByIdAsync(
        long currentUserId,
        IReadOnlyList<string> roles,
        long invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _paymentRepository.GetInvoiceByIdAsync(invoiceId, cancellationToken);
        if (invoice == null)
        {
            throw new NotFoundException($"Không tìm thấy hóa đơn với ID {invoiceId}.");
        }

        // Authorization check
        var isAdmin = roles.Contains(RoleConstants.SystemAdministrator) || roles.Contains(RoleConstants.BusinessOperationsManager);
        var isOwner = roles.Contains(RoleConstants.StorageCustomer) && invoice.customer_id == currentUserId;

        if (!isAdmin && !isOwner)
        {
            var facilityId = invoice.reservation?.facility_id ?? invoice.agreement?.facility_id;
            var isStaff = roles.Any(r => r == RoleConstants.FacilityStaff || r == RoleConstants.FacilityManager);

            if (facilityId.HasValue && isStaff)
            {
                var hasAccess = await _facilityScopeService.HasAccessToFacilityAsync(
                    currentUserId,
                    facilityId.Value,
                    roles.First(),
                    cancellationToken);

                if (!hasAccess)
                {
                    throw new ForbiddenException("Bạn không có quyền truy cập vào hóa đơn của cơ sở này.");
                }
            }
            else
            {
                throw new ForbiddenException("Bạn không có quyền xem thông tin hóa đơn này.");
            }
        }

        return new InvoiceDetailResponse
        {
            InvoiceId = invoice.id,
            InvoiceNo = invoice.invoice_no,
            CustomerId = invoice.customer_id,
            CustomerName = invoice.customer?.full_name ?? "Khách hàng",
            ReservationId = invoice.reservation_id,
            ReservationCode = invoice.reservation?.reservation_code,
            AgreementId = invoice.agreement_id,
            AgreementNo = invoice.agreement?.agreement_no,
            BillingPeriod = invoice.billing_period,
            IssueDate = invoice.issue_date,
            DueDate = invoice.due_date,
            Currency = invoice.currency,
            SubtotalAmount = invoice.subtotal_amount,
            DiscountAmount = invoice.discount_amount,
            TaxAmount = invoice.tax_amount,
            TotalAmount = invoice.total_amount,
            PaidAmount = invoice.paid_amount,
            Status = invoice.status,
            OpenedAt = invoice.opened_at,
            CreatedAt = invoice.created_at,
            Lines = invoice.invoice_lines.Select(l => new InvoiceLineResponse
            {
                Id = l.id,
                LineType = l.line_type,
                Description = l.description,
                Quantity = l.quantity,
                UnitPrice = l.unit_price,
                LineAmount = l.line_amount ?? (l.quantity * l.unit_price)
            }).ToList()
        };
    }
}
