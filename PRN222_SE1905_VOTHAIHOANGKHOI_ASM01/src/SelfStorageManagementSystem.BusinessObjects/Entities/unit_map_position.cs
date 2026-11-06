using System;
using System.Collections.Generic;

namespace SelfStorageManagementSystem.DataAccess.Entities;

public partial class unit_map_position
{
    public long unit_id { get; set; }

    public long area_id { get; set; }

    public decimal x { get; set; }

    public decimal y { get; set; }

    public decimal width { get; set; }

    public decimal height { get; set; }

    public decimal rotation_degrees { get; set; }

    public string metadata { get; set; } = null!;

    public virtual facility_area area { get; set; } = null!;

    public virtual storage_unit unit { get; set; } = null!;
}
