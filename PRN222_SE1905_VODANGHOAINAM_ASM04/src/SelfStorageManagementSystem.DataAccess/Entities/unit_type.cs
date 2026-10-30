using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class unit_type
{
    public long id { get; set; }

    public string code { get; set; } = null!;

    public string name { get; set; } = null!;

    public decimal width_m { get; set; }

    public decimal length_m { get; set; }

    public decimal height_m { get; set; }

    public decimal? area_m2 { get; set; }

    public decimal? volume_m3 { get; set; }

    public bool climate_controlled { get; set; }

    public decimal? max_weight_kg { get; set; }

    public string? description { get; set; }

    public bool is_active { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }

    public virtual ICollection<facility_rate> facility_rates { get; set; } = new List<facility_rate>();

    public virtual ICollection<price_range> price_ranges { get; set; } = new List<price_range>();

    public virtual ICollection<reservation> reservations { get; set; } = new List<reservation>();

    public virtual ICollection<storage_unit> storage_units { get; set; } = new List<storage_unit>();

    public virtual ICollection<unit_transfer_request> unit_transfer_requests { get; set; } = new List<unit_transfer_request>();
}
