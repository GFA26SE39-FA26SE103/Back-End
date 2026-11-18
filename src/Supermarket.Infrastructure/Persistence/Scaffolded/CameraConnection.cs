using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class CameraConnection
{
    public Guid ConnectionId { get; set; }

    public Guid CameraId { get; set; }

    public string SourceType { get; set; } = null!;

    public string Protocol { get; set; } = null!;

    public string StreamUri { get; set; } = null!;

    public string? SnapshotUri { get; set; }

    public string? Username { get; set; }

    public string? CredentialSecretRef { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime? LastTestedAt { get; set; }

    public string? LastTestResult { get; set; }

    public string? LastTestMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Camera Camera { get; set; } = null!;
}
