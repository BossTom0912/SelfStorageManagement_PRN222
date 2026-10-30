using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class user_role
{
    public long user_id { get; set; }

    public short role_id { get; set; }

    public long? granted_by { get; set; }

    public DateTimeOffset granted_at { get; set; }

    public virtual user? granted_byNavigation { get; set; }

    public virtual role role { get; set; } = null!;

    public virtual user user { get; set; } = null!;
}
