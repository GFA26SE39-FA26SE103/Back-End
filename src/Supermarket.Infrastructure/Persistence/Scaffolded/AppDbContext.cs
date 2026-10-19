using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Camera> Cameras { get; set; }

    public virtual DbSet<CameraConnection> CameraConnections { get; set; }

    public virtual DbSet<CameraHealthEvent> CameraHealthEvents { get; set; }

    public virtual DbSet<CameraZoneMapping> CameraZoneMappings { get; set; }

    public virtual DbSet<Floor> Floors { get; set; }

    public virtual DbSet<Incident> Incidents { get; set; }

    public virtual DbSet<IncidentType> IncidentTypes { get; set; }

    public virtual DbSet<MonitoringConfiguration> MonitoringConfigurations { get; set; }

    public virtual DbSet<MonitoringRule> MonitoringRules { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationConfig> NotificationConfigs { get; set; }

    public virtual DbSet<NotificationRecipient> NotificationRecipients { get; set; }

    public virtual DbSet<OperationalEvent> OperationalEvents { get; set; }

    public virtual DbSet<OperationalEventType> OperationalEventTypes { get; set; }

    public virtual DbSet<ResponsePolicy> ResponsePolicies { get; set; }

    public virtual DbSet<ResponsePolicyAction> ResponsePolicyActions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Shift> Shifts { get; set; }

    public virtual DbSet<ShiftAssignment> ShiftAssignments { get; set; }

    public virtual DbSet<StaffZoneAssignment> StaffZoneAssignments { get; set; }

    public virtual DbSet<Supermarket> Supermarkets { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<TaskAssignmentHistory> TaskAssignmentHistories { get; set; }

    public virtual DbSet<UserAccount> UserAccounts { get; set; }

    public virtual DbSet<Zone> Zones { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Camera>(entity =>
        {
            entity.ToTable("Camera");

            entity.HasIndex(e => e.FloorId, "IX_Camera_FloorId");

            entity.HasIndex(e => e.HealthStatus, "IX_Camera_HealthStatus");

            entity.HasIndex(e => e.Status, "IX_Camera_Status");

            entity.HasIndex(e => e.Code, "UQ_Camera_Code").IsUnique();

            entity.Property(e => e.CameraId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("camera_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Camera_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.FloorId).HasColumnName("floor_id");
            entity.Property(e => e.HealthStatus)
                .HasMaxLength(20)
                .HasDefaultValue("UNKNOWN", "DF_Camera_HealthStatus")
                .HasColumnName("health_status");
            entity.Property(e => e.InstalledAt)
                .HasPrecision(3)
                .HasColumnName("installed_at");
            entity.Property(e => e.LastMaintenanceAt)
                .HasPrecision(3)
                .HasColumnName("last_maintenance_at");
            entity.Property(e => e.LastMaintenanceNote)
                .HasMaxLength(1000)
                .HasColumnName("last_maintenance_note");
            entity.Property(e => e.LastSeenAt)
                .HasPrecision(3)
                .HasColumnName("last_seen_at");
            entity.Property(e => e.Manufacturer)
                .HasMaxLength(100)
                .HasColumnName("manufacturer");
            entity.Property(e => e.MapRotationDeg)
                .HasColumnType("decimal(6, 2)")
                .HasColumnName("map_rotation_deg");
            entity.Property(e => e.MapX)
                .HasColumnType("decimal(9, 6)")
                .HasColumnName("map_x");
            entity.Property(e => e.MapY)
                .HasColumnType("decimal(9, 6)")
                .HasColumnName("map_y");
            entity.Property(e => e.Model)
                .HasMaxLength(100)
                .HasColumnName("model");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.SerialNumber)
                .HasMaxLength(150)
                .HasColumnName("serial_number");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("INACTIVE", "DF_Camera_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Camera_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.WarrantyExpiresAt)
                .HasPrecision(3)
                .HasColumnName("warranty_expires_at");

            entity.HasOne(d => d.Floor).WithMany(p => p.Cameras)
                .HasForeignKey(d => d.FloorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Camera_Floor");
        });

        modelBuilder.Entity<CameraConnection>(entity =>
        {
            entity.HasKey(e => e.ConnectionId);

            entity.ToTable("CameraConnection");

            entity.HasIndex(e => e.CameraId, "UQ_CameraConnection_Camera").IsUnique();

            entity.Property(e => e.ConnectionId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("connection_id");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CameraConnection_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.CredentialSecretRef)
                .HasMaxLength(500)
                .HasColumnName("credential_secret_ref");
            entity.Property(e => e.IsEnabled).HasColumnName("is_enabled");
            entity.Property(e => e.LastTestMessage)
                .HasMaxLength(500)
                .HasColumnName("last_test_message");
            entity.Property(e => e.LastTestResult)
                .HasMaxLength(20)
                .HasColumnName("last_test_result");
            entity.Property(e => e.LastTestedAt)
                .HasPrecision(3)
                .HasColumnName("last_tested_at");
            entity.Property(e => e.Protocol)
                .HasMaxLength(20)
                .HasColumnName("protocol");
            entity.Property(e => e.SnapshotUri)
                .HasMaxLength(1000)
                .HasColumnName("snapshot_uri");
            entity.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasColumnName("source_type");
            entity.Property(e => e.StreamUri)
                .HasMaxLength(1000)
                .HasColumnName("stream_uri");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CameraConnection_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(150)
                .HasColumnName("username");

            entity.HasOne(d => d.Camera).WithOne(p => p.CameraConnection)
                .HasForeignKey<CameraConnection>(d => d.CameraId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CameraConnection_Camera");
        });

        modelBuilder.Entity<CameraHealthEvent>(entity =>
        {
            entity.HasKey(e => e.HealthEventId);

            entity.ToTable("CameraHealthEvent");

            entity.HasIndex(e => new { e.CameraId, e.DetectedAt }, "IX_CameraHealthEvent_Camera_DetectedAt").IsDescending(false, true);

            entity.HasIndex(e => e.Status, "IX_CameraHealthEvent_Status");

            entity.Property(e => e.HealthEventId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("health_event_id");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.DetectedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CameraHealthEvent_DetectedAt")
                .HasColumnName("detected_at");
            entity.Property(e => e.EventType)
                .HasMaxLength(50)
                .HasColumnName("event_type");
            entity.Property(e => e.InvestigatedAt)
                .HasPrecision(3)
                .HasColumnName("investigated_at");
            entity.Property(e => e.InvestigatedByUserId).HasColumnName("investigated_by_user_id");
            entity.Property(e => e.ResolutionNote)
                .HasMaxLength(1000)
                .HasColumnName("resolution_note");
            entity.Property(e => e.ResolvedAt)
                .HasPrecision(3)
                .HasColumnName("resolved_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("OPEN", "DF_CameraHealthEvent_Status")
                .HasColumnName("status");

            entity.HasOne(d => d.Camera).WithMany(p => p.CameraHealthEvents)
                .HasForeignKey(d => d.CameraId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CameraHealthEvent_Camera");

            entity.HasOne(d => d.InvestigatedByUser).WithMany(p => p.CameraHealthEvents)
                .HasForeignKey(d => d.InvestigatedByUserId)
                .HasConstraintName("FK_CameraHealthEvent_Investigator");
        });

        modelBuilder.Entity<CameraZoneMapping>(entity =>
        {
            entity.HasKey(e => e.CameraZoneId);

            entity.ToTable("CameraZoneMapping");

            entity.HasIndex(e => e.ZoneId, "IX_CameraZoneMapping_ZoneId");

            entity.HasIndex(e => new { e.CameraId, e.ZoneId }, "UQ_CameraZoneMapping").IsUnique();

            entity.Property(e => e.CameraZoneId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("camera_zone_id");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CameraZoneMapping_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.RoiPolygon).HasColumnName("roi_polygon");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE", "DF_CameraZoneMapping_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_CameraZoneMapping_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.Camera).WithMany(p => p.CameraZoneMappings)
                .HasForeignKey(d => d.CameraId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CameraZoneMapping_Camera");

            entity.HasOne(d => d.Zone).WithMany(p => p.CameraZoneMappings)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CameraZoneMapping_Zone");
        });

        modelBuilder.Entity<Floor>(entity =>
        {
            entity.ToTable("Floor");

            entity.HasIndex(e => e.SupermarketId, "IX_Floor_SupermarketId");

            entity.HasIndex(e => new { e.SupermarketId, e.FloorNumber }, "UQ_Floor_Number").IsUnique();

            entity.Property(e => e.FloorId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("floor_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Floor_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.FloorNumber).HasColumnName("floor_number");
            entity.Property(e => e.MapAssetUrl)
                .HasMaxLength(1000)
                .HasColumnName("map_asset_url");
            entity.Property(e => e.MapHeight).HasColumnName("map_height");
            entity.Property(e => e.MapWidth).HasColumnName("map_width");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.SupermarketId).HasColumnName("supermarket_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Floor_UpdatedAt")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Supermarket).WithMany(p => p.Floors)
                .HasForeignKey(d => d.SupermarketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Floor_Supermarket");
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.ToTable("Incident");

            entity.HasIndex(e => new { e.ZoneId, e.IncidentTypeId, e.ClosedAt }, "IX_Incident_Zone_Type_Closed");

            entity.HasIndex(e => new { e.ZoneId, e.IncidentTypeId }, "UX_Incident_Open_Zone_Type")
                .IsUnique()
                .HasFilter("([closed_at] IS NULL)");

            entity.HasIndex(e => e.StaffReportId, "UX_Incident_StaffReport")
                .IsUnique()
                .HasFilter("([staff_report_id] IS NOT NULL)");

            entity.Property(e => e.IncidentId)
                .HasDefaultValueSql("(newsequentialid())", "DF_Incident_Id")
                .HasColumnName("incident_id");
            entity.Property(e => e.ClosedAt)
                .HasPrecision(3)
                .HasColumnName("closed_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Incident_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(2000)
                .HasColumnName("description");
            entity.Property(e => e.IncidentTypeId).HasColumnName("incident_type_id");
            entity.Property(e => e.ReportedByUserId).HasColumnName("reported_by_user_id");
            entity.Property(e => e.Severity)
                .HasMaxLength(20)
                .HasColumnName("severity");
            entity.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasColumnName("source_type");
            entity.Property(e => e.StaffReportId).HasColumnName("staff_report_id");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.StatusReason)
                .HasMaxLength(1000)
                .HasColumnName("status_reason");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.TriggerCameraId).HasColumnName("trigger_camera_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Incident_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.IncidentType).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.IncidentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Incident_Type");

            entity.HasOne(d => d.ReportedByUser).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.ReportedByUserId)
                .HasConstraintName("FK_Incident_Reporter");

            entity.HasOne(d => d.TriggerCamera).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.TriggerCameraId)
                .HasConstraintName("FK_Incident_Camera");

            entity.HasOne(d => d.Zone).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Incident_Zone");
        });

        modelBuilder.Entity<IncidentType>(entity =>
        {
            entity.ToTable("IncidentType");

            entity.HasIndex(e => e.Code, "UQ_IncidentType_Code").IsUnique();

            entity.Property(e => e.IncidentTypeId)
                .HasDefaultValueSql("(newsequentialid())", "DF_IncidentType_Id")
                .HasColumnName("incident_type_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_IncidentType_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.DefaultSeverity)
                .HasMaxLength(20)
                .HasDefaultValue("WARNING", "DF_IncidentType_Severity")
                .HasColumnName("default_severity");
            entity.Property(e => e.Description)
                .HasMaxLength(500)
                .HasColumnName("description");
            entity.Property(e => e.MeasurementType)
                .HasMaxLength(50)
                .HasColumnName("measurement_type");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.RequiresBeforePhoto).HasColumnName("requires_before_photo");
            entity.Property(e => e.SourceType)
                .HasMaxLength(20)
                .HasColumnName("source_type");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE", "DF_IncidentType_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_IncidentType_UpdatedAt")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<MonitoringConfiguration>(entity =>
        {
            entity.HasKey(e => e.ConfigId);

            entity.ToTable("MonitoringConfiguration");

            entity.HasIndex(e => e.ZoneId, "UQ_MonitoringConfiguration_Zone").IsUnique();

            entity.Property(e => e.ConfigId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("config_id");
            entity.Property(e => e.ConfidenceThreshold)
                .HasDefaultValue(0.5000m, "DF_MonitoringConfiguration_Confidence")
                .HasColumnType("decimal(5, 4)")
                .HasColumnName("confidence_threshold");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_MonitoringConfiguration_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("DRAFT", "DF_MonitoringConfiguration_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_MonitoringConfiguration_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.MonitoringConfigurations)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringConfiguration_CreatedBy");

            entity.HasOne(d => d.Zone).WithOne(p => p.MonitoringConfiguration)
                .HasForeignKey<MonitoringConfiguration>(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringConfiguration_Zone");
        });

        modelBuilder.Entity<MonitoringRule>(entity =>
        {
            entity.HasKey(e => e.RuleId);

            entity.ToTable("MonitoringRule");

            entity.HasIndex(e => e.ConfigId, "IX_MonitoringRule_ConfigId");

            entity.HasIndex(e => e.EventTypeId, "IX_MonitoringRule_EventTypeId");

            entity.HasIndex(e => e.IncidentTypeId, "IX_MonitoringRule_IncidentTypeId");

            entity.HasIndex(e => new { e.ConfigId, e.EventTypeId }, "UQ_MonitoringRule_Config_EventType").IsUnique();

            entity.HasIndex(e => new { e.ConfigId, e.IncidentTypeId }, "UQ_MonitoringRule_Config_IncidentType").IsUnique();

            entity.Property(e => e.RuleId)
                .HasDefaultValueSql("(newsequentialid())", "DF_MonitoringRule_Id")
                .HasColumnName("rule_id");
            entity.Property(e => e.ConfigId).HasColumnName("config_id");
            entity.Property(e => e.CooldownSec)
                .HasDefaultValue(300, "DF_MonitoringRule_Cooldown")
                .HasColumnName("cooldown_sec");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_MonitoringRule_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.CriticalThreshold)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("critical_threshold");
            entity.Property(e => e.Enabled)
                .HasDefaultValue(true, "DF_MonitoringRule_Enabled")
                .HasColumnName("enabled");
            entity.Property(e => e.EventTypeId).HasColumnName("event_type_id");
            entity.Property(e => e.IncidentTypeId).HasColumnName("incident_type_id");
            entity.Property(e => e.ParametersJson).HasColumnName("parameters_json");
            entity.Property(e => e.SustainSec)
                .HasDefaultValue(30, "DF_MonitoringRule_Sustain")
                .HasColumnName("sustain_sec");
            entity.Property(e => e.ThresholdUnit)
                .HasMaxLength(30)
                .HasColumnName("threshold_unit");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_MonitoringRule_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.WarningThreshold)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("warning_threshold");

            entity.HasOne(d => d.Config).WithMany(p => p.MonitoringRules)
                .HasForeignKey(d => d.ConfigId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringRule_Configuration");

            entity.HasOne(d => d.EventType).WithMany(p => p.MonitoringRules)
                .HasForeignKey(d => d.EventTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringRule_OperationalEventType");

            entity.HasOne(d => d.IncidentType).WithMany(p => p.MonitoringRules)
                .HasForeignKey(d => d.IncidentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringRule_IncidentType");
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notification");

            entity.Property(e => e.NotificationId)
                .HasDefaultValueSql("(newsequentialid())", "DF_Notification_Id")
                .HasColumnName("notification_id");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Notification_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.EventId).HasColumnName("event_id");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id");
            entity.Property(e => e.Message)
                .HasMaxLength(4000)
                .HasColumnName("message");
            entity.Property(e => e.NotificationType)
                .HasMaxLength(60)
                .HasColumnName("notification_type");
            entity.Property(e => e.Severity)
                .HasMaxLength(40)
                .HasColumnName("severity");
            entity.Property(e => e.TaskId).HasColumnName("task_id");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");

            entity.HasOne(d => d.Camera).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.CameraId)
                .HasConstraintName("FK_Notification_Camera");

            entity.HasOne(d => d.Event).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.EventId)
                .HasConstraintName("FK_Notification_Event");

            entity.HasOne(d => d.Incident).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.IncidentId)
                .HasConstraintName("FK_Notification_Incident");

            entity.HasOne(d => d.Task).WithMany(p => p.Notifications)
                .HasForeignKey(d => d.TaskId)
                .HasConstraintName("FK_Notification_Task");
        });

        modelBuilder.Entity<NotificationConfig>(entity =>
        {
            entity.ToTable("NotificationConfig");

            entity.Property(e => e.NotificationConfigId)
                .HasDefaultValueSql("(newsequentialid())", "DF_NotificationConfig_Id")
                .HasColumnName("notification_config_id");
            entity.Property(e => e.MaxRetryCount).HasColumnName("max_retry_count");
            entity.Property(e => e.RetryIntervalSec).HasColumnName("retry_interval_sec");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_NotificationConfig_UpdatedAt")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<NotificationRecipient>(entity =>
        {
            entity.ToTable("NotificationRecipient");

            entity.HasIndex(e => new { e.NotificationId, e.UserId, e.DeliveryChannel }, "UQ_NotificationRecipient").IsUnique();

            entity.Property(e => e.NotificationRecipientId)
                .HasDefaultValueSql("(newsequentialid())", "DF_NotificationRecipient_Id")
                .HasColumnName("notification_recipient_id");
            entity.Property(e => e.DeliveredAt)
                .HasPrecision(3)
                .HasColumnName("delivered_at");
            entity.Property(e => e.DeliveryChannel)
                .HasMaxLength(40)
                .HasColumnName("delivery_channel");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(40)
                .HasColumnName("delivery_status");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.ReadAt)
                .HasPrecision(3)
                .HasColumnName("read_at");
            entity.Property(e => e.RecipientReason)
                .HasMaxLength(1000)
                .HasColumnName("recipient_reason");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Notification).WithMany(p => p.NotificationRecipients)
                .HasForeignKey(d => d.NotificationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NotificationRecipient_Notification");

            entity.HasOne(d => d.User).WithMany(p => p.NotificationRecipients)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_NotificationRecipient_User");
        });

        modelBuilder.Entity<OperationalEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);

            entity.ToTable("OperationalEvent");

            entity.HasIndex(e => new { e.EventTypeId, e.DetectedAt }, "IX_OperationalEvent_EventType_Time").IsDescending(false, true);

            entity.HasIndex(e => new { e.IncidentId, e.DetectedAt }, "IX_OperationalEvent_Incident_Time").IsDescending(false, true);

            entity.HasIndex(e => new { e.ZoneId, e.DetectedAt }, "IX_OperationalEvent_Zone_Time").IsDescending(false, true);

            entity.HasIndex(e => new { e.CameraId, e.ZoneId, e.EventTypeId }, "UX_OperationalEvent_Active_Camera_Zone_EventType")
                .IsUnique()
                .HasFilter("([cleared_at] IS NULL)");

            entity.Property(e => e.EventId)
                .HasDefaultValueSql("(newsequentialid())", "DF_OperationalEvent_Id")
                .HasColumnName("event_id");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.ClearedAt)
                .HasPrecision(3)
                .HasColumnName("cleared_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_OperationalEvent_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.DetectedAt)
                .HasPrecision(3)
                .HasColumnName("detected_at");
            entity.Property(e => e.EventType)
                .HasMaxLength(50)
                .HasColumnName("event_type");
            entity.Property(e => e.EventTypeId).HasColumnName("event_type_id");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id");
            entity.Property(e => e.LastObservedAt)
                .HasPrecision(3)
                .HasColumnName("last_observed_at");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.MetricValue)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("metric_value");
            entity.Property(e => e.Priority)
                .HasMaxLength(40)
                .HasColumnName("priority");
            entity.Property(e => e.RuleId).HasColumnName("rule_id");
            entity.Property(e => e.Status)
                .HasMaxLength(30)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.Camera).WithMany(p => p.OperationalEvents)
                .HasForeignKey(d => d.CameraId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OperationalEvent_Camera");

            entity.HasOne(d => d.EventTypeNavigation).WithMany(p => p.OperationalEvents)
                .HasForeignKey(d => d.EventTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OperationalEvent_OperationalEventType");

            entity.HasOne(d => d.Incident).WithMany(p => p.OperationalEvents)
                .HasForeignKey(d => d.IncidentId)
                .HasConstraintName("FK_OperationalEvent_Incident");

            entity.HasOne(d => d.Rule).WithMany(p => p.OperationalEvents)
                .HasForeignKey(d => d.RuleId)
                .HasConstraintName("FK_OperationalEvent_Rule");

            entity.HasOne(d => d.Zone).WithMany(p => p.OperationalEvents)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OperationalEvent_Zone");
        });

        modelBuilder.Entity<OperationalEventType>(entity =>
        {
            entity.HasKey(e => e.EventTypeId);

            entity.ToTable("OperationalEventType");

            entity.HasIndex(e => e.Code, "UQ_OperationalEventType_Code").IsUnique();

            entity.Property(e => e.EventTypeId)
                .HasDefaultValueSql("(newsequentialid())", "DF_OperationalEventType_Id")
                .HasColumnName("event_type_id");
            entity.Property(e => e.Code)
                .HasMaxLength(100)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_OperationalEventType_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(1000)
                .HasColumnName("description");
            entity.Property(e => e.MeasurementType)
                .HasMaxLength(100)
                .HasColumnName("measurement_type");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_OperationalEventType_UpdatedAt")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<ResponsePolicy>(entity =>
        {
            entity.HasKey(e => e.PolicyId);

            entity.ToTable("ResponsePolicy");

            entity.HasIndex(e => new { e.RuleId, e.EventPriority }, "UQ_ResponsePolicy_Rule_Priority").IsUnique();

            entity.Property(e => e.PolicyId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ResponsePolicy_Id")
                .HasColumnName("policy_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ResponsePolicy_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.EventPriority)
                .HasMaxLength(40)
                .HasColumnName("event_priority");
            entity.Property(e => e.IsEnabled)
                .HasDefaultValue(true, "DF_ResponsePolicy_Enabled")
                .HasColumnName("is_enabled");
            entity.Property(e => e.RuleId).HasColumnName("rule_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ResponsePolicy_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.Version)
                .HasDefaultValue(1, "DF_ResponsePolicy_Version")
                .HasColumnName("version");

            entity.HasOne(d => d.Rule).WithMany(p => p.ResponsePolicies)
                .HasForeignKey(d => d.RuleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResponsePolicy_Rule");
        });

        modelBuilder.Entity<ResponsePolicyAction>(entity =>
        {
            entity.HasKey(e => e.ActionId);

            entity.ToTable("ResponsePolicyAction");

            entity.Property(e => e.ActionId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ResponsePolicyAction_Id")
                .HasColumnName("action_id");
            entity.Property(e => e.ActionParametersJson).HasColumnName("action_parameters_json");
            entity.Property(e => e.ActionType)
                .HasMaxLength(60)
                .HasColumnName("action_type");
            entity.Property(e => e.ConditionJson).HasColumnName("condition_json");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ResponsePolicyAction_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.ExecutionMode)
                .HasMaxLength(40)
                .HasColumnName("execution_mode");
            entity.Property(e => e.IsEnabled)
                .HasDefaultValue(true, "DF_ResponsePolicyAction_Enabled")
                .HasColumnName("is_enabled");
            entity.Property(e => e.PolicyId).HasColumnName("policy_id");
            entity.Property(e => e.ProposedIncidentSeverity)
                .HasMaxLength(40)
                .HasColumnName("proposed_incident_severity");
            entity.Property(e => e.TargetIncidentTypeId).HasColumnName("target_incident_type_id");
            entity.Property(e => e.TargetRoleId).HasColumnName("target_role_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ResponsePolicyAction_UpdatedAt")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Policy).WithMany(p => p.ResponsePolicyActions)
                .HasForeignKey(d => d.PolicyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResponsePolicyAction_Policy");

            entity.HasOne(d => d.TargetIncidentType).WithMany(p => p.ResponsePolicyActions)
                .HasForeignKey(d => d.TargetIncidentTypeId)
                .HasConstraintName("FK_ResponsePolicyAction_TargetIncidentType");

            entity.HasOne(d => d.TargetRole).WithMany(p => p.ResponsePolicyActions)
                .HasForeignKey(d => d.TargetRoleId)
                .HasConstraintName("FK_ResponsePolicyAction_TargetRole");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Role");

            entity.HasIndex(e => e.Name, "UQ_Role_Name").IsUnique();

            entity.Property(e => e.RoleId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("role_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Role_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.ToTable("Shift");

            entity.Property(e => e.ShiftId)
                .HasDefaultValueSql("(newsequentialid())", "DF_Shift_Id")
                .HasColumnName("shift_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Shift_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.EndTime)
                .HasPrecision(0)
                .HasColumnName("end_time");
            entity.Property(e => e.Name)
                .HasMaxLength(200)
                .HasColumnName("name");
            entity.Property(e => e.StartTime)
                .HasPrecision(0)
                .HasColumnName("start_time");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.SupermarketId).HasColumnName("supermarket_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Shift_UpdatedAt")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Supermarket).WithMany(p => p.Shifts)
                .HasForeignKey(d => d.SupermarketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Shift_Supermarket");
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.ToTable("ShiftAssignment");

            entity.HasIndex(e => new { e.UserId, e.ShiftId, e.WorkDate }, "UQ_ShiftAssignment_User_Shift_Date").IsUnique();

            entity.Property(e => e.ShiftAssignmentId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ShiftAssignment_Id")
                .HasColumnName("shift_assignment_id");
            entity.Property(e => e.CheckInAt)
                .HasPrecision(3)
                .HasColumnName("check_in_at");
            entity.Property(e => e.CheckOutAt)
                .HasPrecision(3)
                .HasColumnName("check_out_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ShiftAssignment_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.ShiftId).HasColumnName("shift_id");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ShiftAssignment_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.WorkDate).HasColumnName("work_date");

            entity.HasOne(d => d.Shift).WithMany(p => p.ShiftAssignments)
                .HasForeignKey(d => d.ShiftId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftAssignment_Shift");

            entity.HasOne(d => d.User).WithMany(p => p.ShiftAssignments)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftAssignment_User");
        });

        modelBuilder.Entity<StaffZoneAssignment>(entity =>
        {
            entity.ToTable("StaffZoneAssignment");

            entity.HasIndex(e => new { e.ShiftAssignmentId, e.ZoneId }, "UX_StaffZoneAssignment_Active")
                .IsUnique()
                .HasFilter("([unassigned_at] IS NULL)");

            entity.Property(e => e.StaffZoneAssignmentId)
                .HasDefaultValueSql("(newsequentialid())", "DF_StaffZoneAssignment_Id")
                .HasColumnName("staff_zone_assignment_id");
            entity.Property(e => e.AssignedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_StaffZoneAssignment_AssignedAt")
                .HasColumnName("assigned_at");
            entity.Property(e => e.ShiftAssignmentId).HasColumnName("shift_assignment_id");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.UnassignedAt)
                .HasPrecision(3)
                .HasColumnName("unassigned_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.ShiftAssignment).WithMany(p => p.StaffZoneAssignments)
                .HasForeignKey(d => d.ShiftAssignmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffZoneAssignment_ShiftAssignment");

            entity.HasOne(d => d.Zone).WithMany(p => p.StaffZoneAssignments)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffZoneAssignment_Zone");
        });

        modelBuilder.Entity<Supermarket>(entity =>
        {
            entity.ToTable("Supermarket");

            entity.HasIndex(e => e.Code, "UQ_Supermarket_Code").IsUnique();

            entity.Property(e => e.SupermarketId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("supermarket_id");
            entity.Property(e => e.Address)
                .HasMaxLength(500)
                .HasColumnName("address");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Supermarket_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.Name)
                .HasMaxLength(150)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE", "DF_Supermarket_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Supermarket_UpdatedAt")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<Task>(entity =>
        {
            entity.ToTable("Task");

            entity.HasIndex(e => new { e.CurrentAssigneeUserId, e.Status }, "IX_Task_Assignee_Status");

            entity.HasIndex(e => e.PlannedStartAt, "IX_Task_PlannedStart_Open").HasFilter("([planned_start_at] IS NOT NULL AND [started_at] IS NULL)");

            entity.HasIndex(e => new { e.ZoneId, e.Status }, "IX_Task_Zone_Status");

            entity.Property(e => e.TaskId)
                .HasDefaultValueSql("(newsequentialid())", "DF_Task_Id")
                .HasColumnName("task_id");
            entity.Property(e => e.AcceptedAt)
                .HasPrecision(3)
                .HasColumnName("accepted_at");
            entity.Property(e => e.AssignedAt)
                .HasPrecision(3)
                .HasColumnName("assigned_at");
            entity.Property(e => e.AssignedByUserId).HasColumnName("assigned_by_user_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Task_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.CurrentAssigneeUserId).HasColumnName("current_assignee_user_id");
            entity.Property(e => e.Description)
                .HasMaxLength(4000)
                .HasColumnName("description");
            entity.Property(e => e.EventId).HasColumnName("event_id");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id");
            entity.Property(e => e.OriginType)
                .HasMaxLength(40)
                .HasColumnName("origin_type");
            entity.Property(e => e.PlannedStartAt)
                .HasPrecision(3)
                .HasColumnName("planned_start_at");
            entity.Property(e => e.Priority)
                .HasMaxLength(40)
                .HasColumnName("priority");
            entity.Property(e => e.StaffReportId).HasColumnName("staff_report_id");
            entity.Property(e => e.StartedAt)
                .HasPrecision(3)
                .HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(60)
                .HasColumnName("status");
            entity.Property(e => e.SubmittedAt)
                .HasPrecision(3)
                .HasColumnName("submitted_at");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Task_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.VerifiedAt)
                .HasPrecision(3)
                .HasColumnName("verified_at");
            entity.Property(e => e.VerifiedByUserId).HasColumnName("verified_by_user_id");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.AssignedByUser).WithMany(p => p.TaskAssignedByUsers)
                .HasForeignKey(d => d.AssignedByUserId)
                .HasConstraintName("FK_Task_AssignedBy");

            entity.HasOne(d => d.CurrentAssigneeUser).WithMany(p => p.TaskCurrentAssigneeUsers)
                .HasForeignKey(d => d.CurrentAssigneeUserId)
                .HasConstraintName("FK_Task_CurrentAssignee");

            entity.HasOne(d => d.Event).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.EventId)
                .HasConstraintName("FK_Task_Event");

            entity.HasOne(d => d.Incident).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.IncidentId)
                .HasConstraintName("FK_Task_Incident");

            entity.HasOne(d => d.VerifiedByUser).WithMany(p => p.TaskVerifiedByUsers)
                .HasForeignKey(d => d.VerifiedByUserId)
                .HasConstraintName("FK_Task_VerifiedBy");

            entity.HasOne(d => d.Zone).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Task_Zone");
        });

        modelBuilder.Entity<TaskAssignmentHistory>(entity =>
        {
            entity.HasKey(e => e.TaskAssignmentId);

            entity.ToTable("TaskAssignmentHistory");

            entity.Property(e => e.TaskAssignmentId)
                .HasDefaultValueSql("(newsequentialid())", "DF_TaskAssignmentHistory_Id")
                .HasColumnName("task_assignment_id");
            entity.Property(e => e.AssignedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_TaskAssignmentHistory_AssignedAt")
                .HasColumnName("assigned_at");
            entity.Property(e => e.AssignedByUserId).HasColumnName("assigned_by_user_id");
            entity.Property(e => e.AssignmentType)
                .HasMaxLength(40)
                .HasColumnName("assignment_type");
            entity.Property(e => e.EndedAt)
                .HasPrecision(3)
                .HasColumnName("ended_at");
            entity.Property(e => e.Reason)
                .HasMaxLength(2000)
                .HasColumnName("reason");
            entity.Property(e => e.RespondedAt)
                .HasPrecision(3)
                .HasColumnName("responded_at");
            entity.Property(e => e.StaffUserId).HasColumnName("staff_user_id");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.TaskId).HasColumnName("task_id");

            entity.HasOne(d => d.AssignedByUser).WithMany(p => p.TaskAssignmentHistoryAssignedByUsers)
                .HasForeignKey(d => d.AssignedByUserId)
                .HasConstraintName("FK_TaskAssignmentHistory_AssignedBy");

            entity.HasOne(d => d.StaffUser).WithMany(p => p.TaskAssignmentHistoryStaffUsers)
                .HasForeignKey(d => d.StaffUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TaskAssignmentHistory_Staff");

            entity.HasOne(d => d.Task).WithMany(p => p.TaskAssignmentHistories)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TaskAssignmentHistory_Task");
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.ToTable("UserAccount");

            entity.HasIndex(e => e.RoleId, "IX_UserAccount_RoleId");

            entity.HasIndex(e => e.Code, "UQ_UserAccount_Code").IsUnique();

            entity.HasIndex(e => e.Email, "UQ_UserAccount_Email").IsUnique();

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("user_id");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserAccount_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(150)
                .HasColumnName("full_name");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(500)
                .HasColumnName("password_hash");
            entity.Property(e => e.Phone)
                .HasMaxLength(30)
                .HasColumnName("phone");
            entity.Property(e => e.RoleId).HasColumnName("role_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE", "DF_UserAccount_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserAccount_UpdatedAt")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Role).WithMany(p => p.UserAccounts)
                .HasForeignKey(d => d.RoleId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserAccount_Role");
        });

        modelBuilder.Entity<Zone>(entity =>
        {
            entity.ToTable("Zone");

            entity.HasIndex(e => e.FloorId, "IX_Zone_FloorId");

            entity.HasIndex(e => new { e.FloorId, e.Code }, "UQ_Zone_Code").IsUnique();

            entity.Property(e => e.ZoneId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("zone_id");
            entity.Property(e => e.AreaM2)
                .HasColumnType("decimal(12, 2)")
                .HasColumnName("area_m2");
            entity.Property(e => e.Code)
                .HasMaxLength(50)
                .HasColumnName("code");
            entity.Property(e => e.ColorHex)
                .HasMaxLength(7)
                .HasColumnName("color_hex");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Zone_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.FloorId).HasColumnName("floor_id");
            entity.Property(e => e.MapPolygon).HasColumnName("map_polygon");
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("ACTIVE", "DF_Zone_Status")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Zone_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneType)
                .HasMaxLength(50)
                .HasColumnName("zone_type");

            entity.HasOne(d => d.Floor).WithMany(p => p.Zones)
                .HasForeignKey(d => d.FloorId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Zone_Floor");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
