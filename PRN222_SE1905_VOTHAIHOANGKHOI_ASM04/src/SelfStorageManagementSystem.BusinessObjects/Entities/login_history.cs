using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class login_history
{
    public long id { get; set; }

    public long? user_id { get; set; }

    public string? attempted_email { get; set; }

    public string result { get; set; } = null!;

    public string? ip_address { get; set; }

    public string? user_agent { get; set; }

    public DateTimeOffset occurred_at { get; set; }

    public virtual user? user { get; set; }
}
