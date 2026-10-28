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

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Camera> Cameras { get; set; }

    public virtual DbSet<CameraConnection> CameraConnections { get; set; }

    public virtual DbSet<CameraHealthEvent> CameraHealthEvents { get; set; }

    public virtual DbSet<CameraZoneMapping> CameraZoneMappings { get; set; }

    public virtual DbSet<Floor> Floors { get; set; }

    public virtual DbSet<Incident> Incidents { get; set; }

    public virtual DbSet<IncidentMedium> IncidentMedia { get; set; }

    public virtual DbSet<IncidentType> IncidentTypes { get; set; }

    public virtual DbSet<MonitoringConfiguration> MonitoringConfigurations { get; set; }

    public virtual DbSet<MonitoringRule> MonitoringRules { get; set; }

    public virtual DbSet<Notification> Notifications { get; set; }

    public virtual DbSet<NotificationRecipient> NotificationRecipients { get; set; }

    public virtual DbSet<OperationalEvent> OperationalEvents { get; set; }

    public virtual DbSet<OperationalEventType> OperationalEventTypes { get; set; }

    public virtual DbSet<Recommendation> Recommendations { get; set; }

    public virtual DbSet<ResponsePolicy> ResponsePolicies { get; set; }

    public virtual DbSet<ResponsePolicyAction> ResponsePolicyActions { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<ServiceHealthEvent> ServiceHealthEvents { get; set; }

    public virtual DbSet<Shift> Shifts { get; set; }

    public virtual DbSet<ShiftAssignment> ShiftAssignments { get; set; }

    public virtual DbSet<ShiftSwapRequest> ShiftSwapRequests { get; set; }

    public virtual DbSet<StaffReport> StaffReports { get; set; }

    public virtual DbSet<StaffReportMedium> StaffReportMedia { get; set; }

    public virtual DbSet<StaffUnavailability> StaffUnavailabilities { get; set; }

    public virtual DbSet<StaffZoneAssignment> StaffZoneAssignments { get; set; }

    public virtual DbSet<Supermarket> Supermarkets { get; set; }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    public virtual DbSet<Task> Tasks { get; set; }

    public virtual DbSet<TaskAssignmentHistory> TaskAssignmentHistories { get; set; }

    public virtual DbSet<TaskEvidence> TaskEvidences { get; set; }

    public virtual DbSet<UserAccount> UserAccounts { get; set; }

    public virtual DbSet<UserDevice> UserDevices { get; set; }

    public virtual DbSet<Zone> Zones { get; set; }

    public virtual DbSet<ZoneAdjacency> ZoneAdjacencies { get; set; }

    public virtual DbSet<ZoneMetric> ZoneMetrics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog");

            entity.HasIndex(e => new { e.EntityType, e.EntityId, e.CreatedAt }, "IX_AuditLog_Entity").IsDescending(false, false, true);

            entity.Property(e => e.AuditLogId)
                .HasDefaultValueSql("(newsequentialid())", "DF_AuditLog_Id")
                .HasColumnName("audit_log_id");
            entity.Property(e => e.Action)
                .HasMaxLength(100)
                .HasColumnName("action");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_AuditLog_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.EntityType)
                .HasMaxLength(100)
                .HasColumnName("entity_type");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.NewValuesJson).HasColumnName("new_values_json");
            entity.Property(e => e.OldValuesJson).HasColumnName("old_values_json");
            entity.Property(e => e.Reason)
                .HasMaxLength(2000)
                .HasColumnName("reason");
            entity.Property(e => e.ResultStatus)
                .HasMaxLength(40)
                .HasColumnName("result_status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_AuditLog_User");
        });

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

            entity.HasIndex(e => e.MonitoringRuleId, "IX_Incident_MonitoringRuleId").HasFilter("([monitoring_rule_id] IS NOT NULL)");

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
            entity.Property(e => e.MonitoringRuleId).HasColumnName("monitoring_rule_id");
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
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Incident_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.IncidentType).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.IncidentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Incident_Type");

            entity.HasOne(d => d.MonitoringRule).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.MonitoringRuleId)
                .HasConstraintName("FK_Incident_MonitoringRule");

            entity.HasOne(d => d.ReportedByUser).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.ReportedByUserId)
                .HasConstraintName("FK_Incident_Reporter");

            entity.HasOne(d => d.StaffReport).WithOne(p => p.Incident)
                .HasForeignKey<Incident>(d => d.StaffReportId)
                .HasConstraintName("FK_Incident_StaffReport");

            entity.HasOne(d => d.Zone).WithMany(p => p.Incidents)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Incident_Zone");
        });

        modelBuilder.Entity<IncidentMedium>(entity =>
        {
            entity.HasKey(e => e.IncidentMediaId);

            entity.Property(e => e.IncidentMediaId)
                .HasDefaultValueSql("(newsequentialid())", "DF_IncidentMedia_Id")
                .HasColumnName("incident_media_id");
            entity.Property(e => e.FileUrl)
                .HasMaxLength(2000)
                .HasColumnName("file_url");
            entity.Property(e => e.IncidentId).HasColumnName("incident_id");
            entity.Property(e => e.MediaType)
                .HasMaxLength(50)
                .HasColumnName("media_type");
            entity.Property(e => e.UploadedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_IncidentMedia_UploadedAt")
                .HasColumnName("uploaded_at");

            entity.HasOne(d => d.Incident).WithMany(p => p.IncidentMedia)
                .HasForeignKey(d => d.IncidentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_IncidentMedia_Incident");
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
            entity.Property(e => e.Name)
                .HasMaxLength(100)
                .HasColumnName("name");
            entity.Property(e => e.RequiresBeforePhoto).HasColumnName("requires_before_photo");
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

            entity.HasIndex(e => new { e.ConfigId, e.EventTypeId }, "UX_MonitoringRule_Config_EventType")
                .IsUnique()
                .HasFilter("([outcome_type]=N'OPERATIONAL_EVENT' AND [event_type_id] IS NOT NULL)");

            entity.HasIndex(e => new { e.ConfigId, e.IncidentTypeId }, "UX_MonitoringRule_Config_IncidentType")
                .IsUnique()
                .HasFilter("([outcome_type]=N'INCIDENT' AND [incident_type_id] IS NOT NULL)");

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
            entity.Property(e => e.OutcomeType)
                .HasMaxLength(40)
                .HasDefaultValue("OPERATIONAL_EVENT", "DF_MonitoringRule_OutcomeType")
                .HasColumnName("outcome_type");
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
                .HasConstraintName("FK_MonitoringRule_OperationalEventType");

            entity.HasOne(d => d.IncidentType).WithMany(p => p.MonitoringRules)
                .HasForeignKey(d => d.IncidentTypeId)
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

        modelBuilder.Entity<NotificationRecipient>(entity =>
        {
            entity.ToTable("NotificationRecipient");

            entity.HasIndex(e => new { e.NotificationId, e.UserId, e.DeliveryChannel }, "UQ_NotificationRecipient").IsUnique();

            entity.Property(e => e.NotificationRecipientId)
                .HasDefaultValueSql("(newsequentialid())", "DF_NotificationRecipient_Id")
                .HasColumnName("notification_recipient_id");
            entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
            entity.Property(e => e.DeliveredAt)
                .HasPrecision(3)
                .HasColumnName("delivered_at");
            entity.Property(e => e.DeliveryChannel)
                .HasMaxLength(40)
                .HasColumnName("delivery_channel");
            entity.Property(e => e.DeliveryStatus)
                .HasMaxLength(40)
                .HasColumnName("delivery_status");
            entity.Property(e => e.LastAttemptAt)
                .HasPrecision(3)
                .HasColumnName("last_attempt_at");
            entity.Property(e => e.NotificationId).HasColumnName("notification_id");
            entity.Property(e => e.ProviderStatus)
                .HasMaxLength(200)
                .HasColumnName("provider_status");
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
            entity.Property(e => e.EventTypeId).HasColumnName("event_type_id");
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

            entity.HasOne(d => d.EventType).WithMany(p => p.OperationalEvents)
                .HasForeignKey(d => d.EventTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_OperationalEvent_OperationalEventType");

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

        modelBuilder.Entity<Recommendation>(entity =>
        {
            entity.ToTable("Recommendation");

            entity.Property(e => e.RecommendationId)
                .HasDefaultValueSql("(newsequentialid())", "DF_Recommendation_Id")
                .HasColumnName("recommendation_id");
            entity.Property(e => e.Description)
                .HasMaxLength(4000)
                .HasColumnName("description");
            entity.Property(e => e.EventId).HasColumnName("event_id");
            entity.Property(e => e.ExpiresAt)
                .HasPrecision(3)
                .HasColumnName("expires_at");
            entity.Property(e => e.GeneratedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_Recommendation_GeneratedAt")
                .HasColumnName("generated_at");
            entity.Property(e => e.RecommendationType)
                .HasMaxLength(100)
                .HasColumnName("recommendation_type");
            entity.Property(e => e.ReviewedAt)
                .HasPrecision(3)
                .HasColumnName("reviewed_at");
            entity.Property(e => e.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.SupermarketId).HasColumnName("supermarket_id");
            entity.Property(e => e.SupportingDataJson).HasColumnName("supporting_data_json");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.Event).WithMany(p => p.Recommendations)
                .HasForeignKey(d => d.EventId)
                .HasConstraintName("FK_Recommendation_Event");

            entity.HasOne(d => d.ReviewedByUser).WithMany(p => p.Recommendations)
                .HasForeignKey(d => d.ReviewedByUserId)
                .HasConstraintName("FK_Recommendation_Reviewer");

            entity.HasOne(d => d.Supermarket).WithMany(p => p.Recommendations)
                .HasForeignKey(d => d.SupermarketId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Recommendation_Supermarket");

            entity.HasOne(d => d.Zone).WithMany(p => p.Recommendations)
                .HasForeignKey(d => d.ZoneId)
                .HasConstraintName("FK_Recommendation_Zone");
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
            entity.Property(e => e.TargetRoleId).HasColumnName("target_role_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ResponsePolicyAction_UpdatedAt")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Policy).WithMany(p => p.ResponsePolicyActions)
                .HasForeignKey(d => d.PolicyId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ResponsePolicyAction_Policy");

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

        modelBuilder.Entity<ServiceHealthEvent>(entity =>
        {
            entity.HasKey(e => e.HealthEventId);

            entity.ToTable("ServiceHealthEvent");

            entity.HasIndex(e => new { e.ServiceName, e.DetectedAt }, "IX_ServiceHealthEvent_Service_DetectedAt").IsDescending(false, true);

            entity.HasIndex(e => new { e.ServiceName, e.Status, e.DetectedAt }, "IX_ServiceHealthEvent_Unresolved")
                .IsDescending(false, false, true)
                .HasFilter("([resolved_at] IS NULL)");

            entity.Property(e => e.HealthEventId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ServiceHealthEvent_Id")
                .HasColumnName("health_event_id");
            entity.Property(e => e.DetectedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ServiceHealthEvent_DetectedAt")
                .HasColumnName("detected_at");
            entity.Property(e => e.ErrorSummary)
                .HasMaxLength(2000)
                .HasColumnName("error_summary");
            entity.Property(e => e.ResolvedAt)
                .HasPrecision(3)
                .HasColumnName("resolved_at");
            entity.Property(e => e.ServiceName)
                .HasMaxLength(200)
                .HasColumnName("service_name");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.ToTable("Shift");

            entity.HasIndex(e => new { e.SupermarketId, e.WeekStart, e.Name }, "UQ_Shift_Supermarket_Week_Name").IsUnique();

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
            entity.Property(e => e.WeekStart).HasColumnName("week_start");

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

        modelBuilder.Entity<ShiftSwapRequest>(entity =>
        {
            entity.HasKey(e => e.SwapRequestId);

            entity.ToTable("ShiftSwapRequest");

            entity.HasIndex(e => e.ExpiresAt, "IX_ShiftSwapRequest_ExpiresAt").HasFilter("([expires_at] IS NOT NULL)");

            entity.HasIndex(e => new { e.RequestedByUserId, e.CreatedAt }, "IX_ShiftSwapRequest_Requester_CreatedAt").IsDescending(false, true);

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_ShiftSwapRequest_Status_CreatedAt").IsDescending(false, true);

            entity.HasIndex(e => new { e.TargetUserId, e.Status }, "IX_ShiftSwapRequest_Target_Status").HasFilter("([target_user_id] IS NOT NULL)");

            entity.Property(e => e.SwapRequestId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ShiftSwapRequest_Id")
                .HasColumnName("swap_request_id");
            entity.Property(e => e.CancellationReason)
                .HasMaxLength(1000)
                .HasColumnName("cancellation_reason");
            entity.Property(e => e.CancelledAt)
                .HasPrecision(3)
                .HasColumnName("cancelled_at");
            entity.Property(e => e.CancelledByUserId).HasColumnName("cancelled_by_user_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ShiftSwapRequest_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.DecidedAt)
                .HasPrecision(3)
                .HasColumnName("decided_at");
            entity.Property(e => e.DecidedByUserId).HasColumnName("decided_by_user_id");
            entity.Property(e => e.DecisionNote)
                .HasMaxLength(1000)
                .HasColumnName("decision_note");
            entity.Property(e => e.ExpiresAt)
                .HasPrecision(3)
                .HasColumnName("expires_at");
            entity.Property(e => e.Note)
                .HasMaxLength(1000)
                .HasColumnName("note");
            entity.Property(e => e.ReasonCode)
                .HasMaxLength(100)
                .HasColumnName("reason_code");
            entity.Property(e => e.RequestType)
                .HasMaxLength(40)
                .HasColumnName("request_type");
            entity.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
            entity.Property(e => e.RequesterShiftAssignmentId).HasColumnName("requester_shift_assignment_id");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.TargetRespondedAt)
                .HasPrecision(3)
                .HasColumnName("target_responded_at");
            entity.Property(e => e.TargetShiftAssignmentId).HasColumnName("target_shift_assignment_id");
            entity.Property(e => e.TargetUserId).HasColumnName("target_user_id");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ShiftSwapRequest_UpdatedAt")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.CancelledByUser).WithMany(p => p.ShiftSwapRequestCancelledByUsers)
                .HasForeignKey(d => d.CancelledByUserId)
                .HasConstraintName("FK_ShiftSwapRequest_CancelledBy");

            entity.HasOne(d => d.DecidedByUser).WithMany(p => p.ShiftSwapRequestDecidedByUsers)
                .HasForeignKey(d => d.DecidedByUserId)
                .HasConstraintName("FK_ShiftSwapRequest_Decider");

            entity.HasOne(d => d.RequestedByUser).WithMany(p => p.ShiftSwapRequestRequestedByUsers)
                .HasForeignKey(d => d.RequestedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftSwapRequest_Requester");

            entity.HasOne(d => d.RequesterShiftAssignment).WithMany(p => p.ShiftSwapRequestRequesterShiftAssignments)
                .HasForeignKey(d => d.RequesterShiftAssignmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ShiftSwapRequest_RequesterAssignment");

            entity.HasOne(d => d.TargetShiftAssignment).WithMany(p => p.ShiftSwapRequestTargetShiftAssignments)
                .HasForeignKey(d => d.TargetShiftAssignmentId)
                .HasConstraintName("FK_ShiftSwapRequest_TargetAssignment");

            entity.HasOne(d => d.TargetUser).WithMany(p => p.ShiftSwapRequestTargetUsers)
                .HasForeignKey(d => d.TargetUserId)
                .HasConstraintName("FK_ShiftSwapRequest_TargetUser");
        });

        modelBuilder.Entity<StaffReport>(entity =>
        {
            entity.HasKey(e => e.ReportId);

            entity.ToTable("StaffReport");

            entity.Property(e => e.ReportId)
                .HasDefaultValueSql("(newsequentialid())", "DF_StaffReport_Id")
                .HasColumnName("report_id");
            entity.Property(e => e.AiExtractedJson).HasColumnName("ai_extracted_json");
            entity.Property(e => e.Classification)
                .HasMaxLength(100)
                .HasColumnName("classification");
            entity.Property(e => e.ClassifiedAt)
                .HasPrecision(3)
                .HasColumnName("classified_at");
            entity.Property(e => e.ConfirmedAt)
                .HasPrecision(3)
                .HasColumnName("confirmed_at");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_StaffReport_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.Description)
                .HasMaxLength(4000)
                .HasColumnName("description");
            entity.Property(e => e.OriginalInputJson).HasColumnName("original_input_json");
            entity.Property(e => e.ReportCategory)
                .HasMaxLength(100)
                .HasColumnName("report_category");
            entity.Property(e => e.ReportedByUserId).HasColumnName("reported_by_user_id");
            entity.Property(e => e.Status)
                .HasMaxLength(40)
                .HasColumnName("status");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_StaffReport_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.ReportedByUser).WithMany(p => p.StaffReports)
                .HasForeignKey(d => d.ReportedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffReport_Reporter");

            entity.HasOne(d => d.Zone).WithMany(p => p.StaffReports)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffReport_Zone");
        });

        modelBuilder.Entity<StaffReportMedium>(entity =>
        {
            entity.HasKey(e => e.MediaId);

            entity.HasIndex(e => new { e.ReportId, e.UploadedAt }, "IX_StaffReportMedia_Report_UploadedAt").IsDescending(false, true);

            entity.Property(e => e.MediaId)
                .HasDefaultValueSql("(newsequentialid())", "DF_StaffReportMedia_Id")
                .HasColumnName("media_id");
            entity.Property(e => e.ContentType)
                .HasMaxLength(200)
                .HasColumnName("content_type");
            entity.Property(e => e.MediaType)
                .HasMaxLength(50)
                .HasColumnName("media_type");
            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.StorageKey)
                .HasMaxLength(1000)
                .HasColumnName("storage_key");
            entity.Property(e => e.UploadedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_StaffReportMedia_UploadedAt")
                .HasColumnName("uploaded_at");
            entity.Property(e => e.UploadedByUserId).HasColumnName("uploaded_by_user_id");

            entity.HasOne(d => d.Report).WithMany(p => p.StaffReportMedia)
                .HasForeignKey(d => d.ReportId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffReportMedia_Report");

            entity.HasOne(d => d.UploadedByUser).WithMany(p => p.StaffReportMedia)
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffReportMedia_Uploader");
        });

        modelBuilder.Entity<StaffUnavailability>(entity =>
        {
            entity.HasKey(e => e.UnavailabilityId);

            entity.ToTable("StaffUnavailability");

            entity.Property(e => e.UnavailabilityId)
                .HasDefaultValueSql("(newsequentialid())", "DF_StaffUnavailability_Id")
                .HasColumnName("unavailability_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_StaffUnavailability_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.EndAt)
                .HasPrecision(3)
                .HasColumnName("end_at");
            entity.Property(e => e.Note)
                .HasMaxLength(1000)
                .HasColumnName("note");
            entity.Property(e => e.StartAt)
                .HasPrecision(3)
                .HasColumnName("start_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.StaffUnavailabilities)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StaffUnavailability_User");
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

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.SettingId);

            entity.ToTable("SystemSetting");

            entity.HasIndex(e => e.SettingKey, "UQ_SystemSetting_Key").IsUnique();

            entity.Property(e => e.SettingId)
                .HasDefaultValueSql("(newsequentialid())", "DF_SystemSetting_Id")
                .HasColumnName("setting_id");
            entity.Property(e => e.SettingKey)
                .HasMaxLength(200)
                .HasColumnName("setting_key");
            entity.Property(e => e.SettingValue).HasColumnName("setting_value");
            entity.Property(e => e.UpdatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_SystemSetting_UpdatedAt")
                .HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");
            entity.Property(e => e.ValueType)
                .HasMaxLength(40)
                .HasColumnName("value_type");

            entity.HasOne(d => d.UpdatedByUser).WithMany(p => p.SystemSettings)
                .HasForeignKey(d => d.UpdatedByUserId)
                .HasConstraintName("FK_SystemSetting_UpdatedBy");
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
            entity.Property(e => e.RejectionCount).HasColumnName("rejection_count");
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

            entity.HasOne(d => d.StaffReport).WithMany(p => p.Tasks)
                .HasForeignKey(d => d.StaffReportId)
                .HasConstraintName("FK_Task_StaffReport");

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

        modelBuilder.Entity<TaskEvidence>(entity =>
        {
            entity.HasKey(e => e.EvidenceId);

            entity.ToTable("TaskEvidence");

            entity.Property(e => e.EvidenceId)
                .HasDefaultValueSql("(newsequentialid())", "DF_TaskEvidence_Id")
                .HasColumnName("evidence_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_TaskEvidence_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.EvidenceImage)
                .HasMaxLength(2000)
                .HasColumnName("evidence_image");
            entity.Property(e => e.EvidenceType)
                .HasMaxLength(50)
                .HasColumnName("evidence_type");
            entity.Property(e => e.Note)
                .HasMaxLength(2000)
                .HasColumnName("note");
            entity.Property(e => e.RejectReason)
                .HasMaxLength(2000)
                .HasColumnName("reject_reason");
            entity.Property(e => e.ReviewStatus)
                .HasMaxLength(40)
                .HasColumnName("review_status");
            entity.Property(e => e.SubmittedByUserId).HasColumnName("submitted_by_user_id");
            entity.Property(e => e.TaskId).HasColumnName("task_id");

            entity.HasOne(d => d.SubmittedByUser).WithMany(p => p.TaskEvidences)
                .HasForeignKey(d => d.SubmittedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TaskEvidence_Submitter");

            entity.HasOne(d => d.Task).WithMany(p => p.TaskEvidences)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TaskEvidence_Task");
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

        modelBuilder.Entity<UserDevice>(entity =>
        {
            entity.HasKey(e => e.DeviceId);

            entity.ToTable("UserDevice");

            entity.HasIndex(e => new { e.UserId, e.RevokedAt }, "IX_UserDevice_User_RevokedAt");

            entity.HasIndex(e => e.FcmToken, "UQ_UserDevice_FcmToken").IsUnique();

            entity.Property(e => e.DeviceId)
                .HasDefaultValueSql("(newsequentialid())", "DF_UserDevice_Id")
                .HasColumnName("device_id");
            entity.Property(e => e.AppVersion)
                .HasMaxLength(100)
                .HasColumnName("app_version");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_UserDevice_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.FcmToken)
                .HasMaxLength(512)
                .HasColumnName("fcm_token");
            entity.Property(e => e.LastSeenAt)
                .HasPrecision(3)
                .HasColumnName("last_seen_at");
            entity.Property(e => e.Platform)
                .HasMaxLength(40)
                .HasColumnName("platform");
            entity.Property(e => e.RevokedAt)
                .HasPrecision(3)
                .HasColumnName("revoked_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserDevices)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UserDevice_User");
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
            entity.Property(e => e.MinStaff).HasColumnName("min_staff");
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

        modelBuilder.Entity<ZoneAdjacency>(entity =>
        {
            entity.ToTable("ZoneAdjacency");

            entity.HasIndex(e => new { e.ZoneId, e.AdjacentZoneId }, "UQ_ZoneAdjacency_Pair").IsUnique();

            entity.Property(e => e.ZoneAdjacencyId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ZoneAdjacency_Id")
                .HasColumnName("zone_adjacency_id");
            entity.Property(e => e.AdjacentZoneId).HasColumnName("adjacent_zone_id");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ZoneAdjacency_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.AdjacentZone).WithMany(p => p.ZoneAdjacencyAdjacentZones)
                .HasForeignKey(d => d.AdjacentZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ZoneAdjacency_AdjacentZone");

            entity.HasOne(d => d.Zone).WithMany(p => p.ZoneAdjacencyZones)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ZoneAdjacency_Zone");
        });

        modelBuilder.Entity<ZoneMetric>(entity =>
        {
            entity.HasKey(e => e.MetricId);

            entity.ToTable("ZoneMetric");

            entity.HasIndex(e => new { e.ZoneId, e.MetricType, e.PeriodStart }, "IX_ZoneMetric_Zone_Type_Period").IsDescending(false, false, true);

            entity.Property(e => e.MetricId)
                .HasDefaultValueSql("(newsequentialid())", "DF_ZoneMetric_Id")
                .HasColumnName("metric_id");
            entity.Property(e => e.CameraId).HasColumnName("camera_id");
            entity.Property(e => e.ConfigVersion).HasColumnName("config_version");
            entity.Property(e => e.CreatedAt)
                .HasPrecision(3)
                .HasDefaultValueSql("(sysutcdatetime())", "DF_ZoneMetric_CreatedAt")
                .HasColumnName("created_at");
            entity.Property(e => e.MetadataJson).HasColumnName("metadata_json");
            entity.Property(e => e.MetricType)
                .HasMaxLength(100)
                .HasColumnName("metric_type");
            entity.Property(e => e.MetricValue)
                .HasColumnType("decimal(18, 4)")
                .HasColumnName("metric_value");
            entity.Property(e => e.PeriodEnd)
                .HasPrecision(3)
                .HasColumnName("period_end");
            entity.Property(e => e.PeriodStart)
                .HasPrecision(3)
                .HasColumnName("period_start");
            entity.Property(e => e.Unit)
                .HasMaxLength(60)
                .HasColumnName("unit");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.Camera).WithMany(p => p.ZoneMetrics)
                .HasForeignKey(d => d.CameraId)
                .HasConstraintName("FK_ZoneMetric_Camera");

            entity.HasOne(d => d.Zone).WithMany(p => p.ZoneMetrics)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ZoneMetric_Zone");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
