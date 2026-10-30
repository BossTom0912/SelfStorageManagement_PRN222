using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class facility_area
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public long? parent_area_id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public string area_type { get; set; } = null!;

    public int display_order { get; set; }

    public string map_metadata { get; set; } = null!;

    public bool is_active { get; set; }

    public virtual ICollection<facility_area> Inversefacility_areaNavigation { get; set; } = new List<facility_area>();

    public virtual ICollection<facility_area> Inverseparent_area { get; set; } = new List<facility_area>();

    public virtual facility facility { get; set; } = null!;

    public virtual facility_area? facility_areaNavigation { get; set; }

    public virtual facility_area? parent_area { get; set; }

    public virtual ICollection<storage_unit> storage_units { get; set; } = new List<storage_unit>();

    public virtual ICollection<unit_map_position> unit_map_positions { get; set; } = new List<unit_map_position>();
}
