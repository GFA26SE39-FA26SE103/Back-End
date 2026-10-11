using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class NotificationConfig
{
    public Guid NotificationConfigId { get; set; }

    public int RetryIntervalSec { get; set; }

    public int MaxRetryCount { get; set; }

    public DateTime UpdatedAt { get; set; }
}
