using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class identity_verification
{
    public long id { get; set; }

    public long reservation_id { get; set; }

    public long verified_by { get; set; }

    public string verification_method { get; set; } = null!;

    public string? document_fingerprint { get; set; }

    public string result { get; set; } = null!;

    public string? notes { get; set; }

    public DateTimeOffset verified_at { get; set; }

    public virtual reservation reservation { get; set; } = null!;

    public virtual employee_profile verified_byNavigation { get; set; } = null!;
}
