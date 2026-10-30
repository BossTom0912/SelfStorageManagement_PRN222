using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class facility
{
    public long id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public string address_line { get; set; } = null!;

    public string? ward { get; set; }

    public string? district { get; set; }

    public string city { get; set; } = null!;

    public decimal? latitude { get; set; }

    public decimal? longitude { get; set; }

    public string timezone { get; set; } = null!;

    public TimeOnly? opening_time { get; set; }

    public TimeOnly? closing_time { get; set; }

    public string status { get; set; } = null!;

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<access_point> access_points { get; set; } = new List<access_point>();

    public virtual ICollection<appointment> appointments { get; set; } = new List<appointment>();

    public virtual ICollection<facility_area> facility_areas { get; set; } = new List<facility_area>();

    public virtual ICollection<facility_rate> facility_rates { get; set; } = new List<facility_rate>();

    public virtual ICollection<fee_rule> fee_rules { get; set; } = new List<fee_rule>();

    public virtual ICollection<inspection> inspections { get; set; } = new List<inspection>();

    public virtual ICollection<maintenance_work_order> maintenance_work_orders { get; set; } = new List<maintenance_work_order>();

    public virtual ICollection<rental_agreement> rental_agreements { get; set; } = new List<rental_agreement>();

    public virtual ICollection<reservation> reservations { get; set; } = new List<reservation>();

    public virtual ICollection<staff_facility_assignment> staff_facility_assignments { get; set; } = new List<staff_facility_assignment>();

    public virtual ICollection<staff_shift> staff_shifts { get; set; } = new List<staff_shift>();

    public virtual ICollection<staff_task> staff_tasks { get; set; } = new List<staff_task>();

    public virtual ICollection<storage_unit> storage_units { get; set; } = new List<storage_unit>();

    public virtual ICollection<support_ticket> support_tickets { get; set; } = new List<support_ticket>();
}
