using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class UnitTypeHaTDT
{
    public long UnitTypeHaTDTId { get; set; }

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
}
