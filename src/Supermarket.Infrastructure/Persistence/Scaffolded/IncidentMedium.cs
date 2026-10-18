using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class IncidentMedium
{
    public Guid IncidentMediaId { get; set; }

    public Guid IncidentId { get; set; }

    public string MediaType { get; set; } = null!;

    public string FileUrl { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public virtual Incident Incident { get; set; } = null!;
}
