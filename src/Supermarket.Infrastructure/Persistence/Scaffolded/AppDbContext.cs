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

    public virtual DbSet<IncidentType> IncidentTypes { get; set; }

    public virtual DbSet<MonitoringConfiguration> MonitoringConfigurations { get; set; }

    public virtual DbSet<MonitoringRule> MonitoringRules { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Supermarket> Supermarkets { get; set; }

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

            entity.HasIndex(e => e.IncidentTypeId, "IX_MonitoringRule_IncidentTypeId");

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

            entity.HasOne(d => d.IncidentType).WithMany(p => p.MonitoringRules)
                .HasForeignKey(d => d.IncidentTypeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_MonitoringRule_IncidentType");
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

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.ToTable("UserAccount");

            entity.HasIndex(e => e.RoleId, "IX_UserAccount_RoleId");

            entity.HasIndex(e => e.Email, "UQ_UserAccount_Email").IsUnique();

            entity.Property(e => e.UserId)
                .HasDefaultValueSql("(newsequentialid())")
                .HasColumnName("user_id");
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
