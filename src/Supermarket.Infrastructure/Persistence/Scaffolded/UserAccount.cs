using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class UserAccount
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public string Email { get; set; } = null!;

    public string PasswordHash { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? Phone { get; set; }

    public string Code { get; set; } = null!;

    public virtual ICollection<CameraHealthEvent> CameraHealthEvents { get; set; } = new List<CameraHealthEvent>();

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<MonitoringConfiguration> MonitoringConfigurations { get; set; } = new List<MonitoringConfiguration>();

    public virtual ICollection<NotificationRecipient> NotificationRecipients { get; set; } = new List<NotificationRecipient>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<ShiftAssignment> ShiftAssignments { get; set; } = new List<ShiftAssignment>();

    public virtual ICollection<Task> TaskAssignedByUsers { get; set; } = new List<Task>();

    public virtual ICollection<TaskAssignmentHistory> TaskAssignmentHistoryAssignedByUsers { get; set; } = new List<TaskAssignmentHistory>();

    public virtual ICollection<TaskAssignmentHistory> TaskAssignmentHistoryStaffUsers { get; set; } = new List<TaskAssignmentHistory>();

    public virtual ICollection<Task> TaskCurrentAssigneeUsers { get; set; } = new List<Task>();

    public virtual ICollection<Task> TaskVerifiedByUsers { get; set; } = new List<Task>();
}
