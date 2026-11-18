using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class SystemSetting
{
    public Guid SettingId { get; set; }

    public string SettingKey { get; set; } = null!;

    public string SettingValue { get; set; } = null!;

    public string ValueType { get; set; } = null!;

    public Guid? UpdatedByUserId { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UserAccount? UpdatedByUser { get; set; }
}
