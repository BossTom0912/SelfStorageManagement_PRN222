using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class storage_unit
{
    public long id { get; set; }

    public long facility_id { get; set; }

    public long unit_type_id { get; set; }

    public long? area_id { get; set; }

    public string unit_code { get; set; } = null!;

    public string? floor_label { get; set; }

    public string? zone_label { get; set; }

    public string physical_status { get; set; } = null!;

    public bool is_listed { get; set; }

    public string? notes { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual facility_area? area { get; set; }

    public virtual facility facility { get; set; } = null!;

    public virtual ICollection<inspection> inspections { get; set; } = new List<inspection>();

    public virtual ICollection<maintenance_work_order> maintenance_work_orders { get; set; } = new List<maintenance_work_order>();

    public virtual ICollection<support_ticket> support_ticketstorage_unitNavigations { get; set; } = new List<support_ticket>();

    public virtual ICollection<support_ticket> support_ticketstorage_units { get; set; } = new List<support_ticket>();

    public virtual ICollection<unit_allocation> unit_allocations { get; set; } = new List<unit_allocation>();

    public virtual unit_map_position? unit_map_position { get; set; }

    public virtual ICollection<unit_status_history> unit_status_histories { get; set; } = new List<unit_status_history>();

    public virtual ICollection<unit_transfer_request> unit_transfer_requestfrom_units { get; set; } = new List<unit_transfer_request>();

    public virtual ICollection<unit_transfer_request> unit_transfer_requestto_units { get; set; } = new List<unit_transfer_request>();

    public virtual unit_type unit_type { get; set; } = null!;
}
