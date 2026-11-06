using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class role
{
    public short id { get; set; }

    public string code { get; set; } = null!;

    public string display_name { get; set; } = null!;

    public string? description { get; set; }

    public virtual ICollection<user_role> user_roles { get; set; } = new List<user_role>();
}
