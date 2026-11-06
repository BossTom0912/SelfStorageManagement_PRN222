using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class access_point
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public string access_point_type { get; set; } = null!;

    public string? external_device_code { get; set; }

    public string status { get; set; } = null!;

    public virtual ICollection<access_event> access_events { get; set; } = new List<access_event>();

    public virtual facility facility { get; set; } = null!;
}
