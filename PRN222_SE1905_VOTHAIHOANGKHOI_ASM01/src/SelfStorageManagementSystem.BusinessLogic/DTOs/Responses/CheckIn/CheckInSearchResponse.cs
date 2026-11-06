namespace SelfStorageManagementSystem.BusinessLogic.DTOs.Responses.CheckIn;

public class CheckInSearchResponse
{
    public long ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public string ReservationStatus { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Customer profile info (che số giấy tờ; không trả secret_digest)
    public long CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? MaskedIdentityNumber { get; set; }
    public bool HasIdentityNumber { get; set; }

    // Facility & Unit Type
    public long FacilityId { get; set; }
    public string FacilityCode { get; set; } = string.Empty;
    public string FacilityName { get; set; } = string.Empty;
    public string FacilityTimezone { get; set; } = string.Empty;
    public long UnitTypeId { get; set; }
    public string UnitTypeName { get; set; } = string.Empty;
    public decimal AreaM2 { get; set; }
    public bool ClimateControlled { get; set; }

    // Agreement info
    public long? AgreementId { get; set; }
    public string? AgreementNo { get; set; }
    public string? AgreementStatus { get; set; }
    public decimal MonthlyRateSnapshot { get; set; }
    public decimal DepositBalance { get; set; }

    // First period invoice / payment status
    public long? CheckoutInvoiceId { get; set; }
    public string? CheckoutInvoiceNo { get; set; }
    public string? InvoiceStatus { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public bool IsFullyPaid { get; set; }

    // Date & readiness check
    public DateOnly FacilityToday { get; set; }
    public bool IsDateEligible { get; set; }
    public bool CanProceedToCheckIn { get; set; }
    public List<string> ReasonsCannotProceed { get; set; } = new();

    // Progress details for reconnection / resume workflow
    public CheckInVerificationSummaryDto? LatestVerification { get; set; }
    public CheckInAllocationSummaryDto? ActiveAllocation { get; set; }
    public CheckInInspectionSummaryDto? LatestInspection { get; set; }
    public CheckInHandoverSummaryDto? HandoverRecord { get; set; }
    public CheckInCredentialSummaryDto? ActiveCredential { get; set; }
}

public class CheckInVerificationSummaryDto
{
    public long VerificationId { get; set; }
    public string VerificationMethod { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; set; }
    public long VerifiedBy { get; set; }
    public string? VerifiedByName { get; set; }
}

public class CheckInAllocationSummaryDto
{
    public long AllocationId { get; set; }
    public long StorageUnitId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string AllocationKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CheckInInspectionSummaryDto
{
    public long InspectionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? OverallCondition { get; set; }
    public DateTimeOffset? InspectedAt { get; set; }
    public string? Summary { get; set; }
    public bool HasPassed { get; set; }
}

public class CheckInHandoverSummaryDto
{
    public long HandoverRecordId { get; set; }
    public string HandoverType { get; set; } = string.Empty;
    public DateTimeOffset HandedAt { get; set; }
    public bool IsCompleted { get; set; }
}

public class CheckInCredentialSummaryDto
{
    public long CredentialId { get; set; }
    public string CredentialType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
