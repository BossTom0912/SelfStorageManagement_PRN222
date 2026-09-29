using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class policy_version
{
    public long id { get; set; }

    public string policy_type { get; set; } = null!;

    public string version { get; set; } = null!;

    public string content { get; set; } = null!;

    public DateOnly valid_from { get; set; }

    public DateOnly? valid_to { get; set; }

    public long? created_by { get; set; }

    public DateTimeOffset created_at { get; set; }

    public virtual user? created_byNavigation { get; set; }

    public virtual ICollection<rental_agreement> rental_agreements { get; set; } = new List<rental_agreement>();
}
