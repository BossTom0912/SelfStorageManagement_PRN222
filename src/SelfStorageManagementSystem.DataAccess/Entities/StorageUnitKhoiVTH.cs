using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class StorageUnitKhoiVTH
{
    public long StorageUnitKhoiVTHId { get; set; }

    public long facility_id { get; set; }

    public long UnitTypeKhoiVTHId { get; set; }

    public long? area_id { get; set; }

    public string unit_code { get; set; } = null!;

    public string? floor_label { get; set; }

    public string? zone_label { get; set; }

    public string physical_status { get; set; } = null!;

    public bool is_listed { get; set; }

    public string? notes { get; set; }

    public DateTimeOffset created_at { get; set; }

    public DateTimeOffset updated_at { get; set; }
}
