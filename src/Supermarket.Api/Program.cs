using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Supermarket.Api;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure;
using Supermarket.Infrastructure.Ai;
using Supermarket.Infrastructure.FloorPlans;
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Supermarket.Infrastructure.Video;
using Supermarket.Infrastructure.Monitoring;
using UserAccount = Supermarket.Domain.UserAccount;
using Role = Supermarket.Domain.Role;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
builder.Services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection("Jwt")).Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Set Jwt:Key to a secret containing at least 32 UTF-8 bytes.").Validate(o => o.LifetimeMinutes is > 0 and <= 1440, "JWT lifetime must be 1 to 1440 minutes.").ValidateOnStart();
builder.Services.Configure<VideoOptions>(builder.Configuration.GetSection("Video"));
builder.Services.PostConfigure<VideoOptions>(options =>
    options.RecordedRoot = Path.GetFullPath(options.RecordedRoot, builder.Environment.ContentRootPath));
builder.Services.AddOptions<FloorPlanOptions>().Bind(builder.Configuration.GetSection("FloorPlan"))
    .Validate(o => o.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase) || o.Provider.Equals("Cloudinary", StringComparison.OrdinalIgnoreCase), "FloorPlan:Provider must be Local or Cloudinary.")
    .Validate(o => o.MaxBytes is > 0 and <= 20971520, "FloorPlan:MaxBytes must be between 1 and 20971520.")
    .ValidateOnStart();
builder.Services.PostConfigure<FloorPlanOptions>(options =>
    options.Root = Path.GetFullPath(options.Root, builder.Environment.ContentRootPath));
builder.Services.AddOptions<CloudinaryOptions>().Bind(builder.Configuration.GetSection("Cloudinary"))
    .Validate(o => !string.Equals(builder.Configuration["FloorPlan:Provider"], "Cloudinary", StringComparison.OrdinalIgnoreCase) || o.IsValid(),
        "Set Cloudinary:CloudName, ApiKey, ApiSecret, Folder and TimeoutSeconds (1-120) before enabling the Cloudinary floor-plan provider.")
    .ValidateOnStart();
builder.Services.Configure<HealthWorkerOptions>(builder.Configuration.GetSection("CameraHealth"));
builder.Services.Configure<MonitoringWorkerOptions>(builder.Configuration.GetSection("Monitoring"));
builder.Services.AddOptions<AiPreviewOptions>().Bind(builder.Configuration.GetSection("AiPreview"))
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https", "AiPreview:BaseUrl must be an absolute HTTP(S) URI.")
    .Validate(o => o.TimeoutSeconds is > 0 and <= 120, "AiPreview timeout must be 1 to 120 seconds.")
    .Validate(o => o.Confidence is >= 0 and <= 1, "AiPreview confidence must be in [0,1].")
    .ValidateOnStart();
builder.Services.AddDbContext<AppDbContext>(o =>
{
    var connectionString = builder.Configuration.GetConnectionString("SqlServer");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException("Set ConnectionStrings:SqlServer in appsettings.Local.json, appsettings.Production.json, or ConnectionStrings__SqlServer.");
    o.UseSqlServer(connectionString);
});
var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local", "keys");
builder.Services.AddDataProtection().SetApplicationName("FA26SE103").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<ICredentialProtector, CredentialProtector>();
builder.Services.AddSingleton<DemoCameraState>();
builder.Services.AddScoped<ICameraStream, CameraStream>();
builder.Services.AddScoped<IRecordedVideoStorage, RecordedVideoStorage>();
builder.Services.AddScoped<RecordedVideoUpload>();
builder.Services.AddScoped<LocalFloorPlanStorage>();
builder.Services.AddHttpClient<CloudinaryFloorPlanStorage>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IFloorPlanStorage, FloorPlanStorage>();
builder.Services.AddScoped<FloorPlanUpload>();
builder.Services.AddScoped<ISetupStore, EfSetupStore>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITokenIssuer, TokenIssuer>();
builder.Services.AddScoped<Accounts>();
builder.Services.AddScoped<StoreSetup>();
builder.Services.AddScoped<CameraSetup>();
builder.Services.AddScoped<MonitoringSetup>();
builder.Services.AddScoped<IMonitoringSnapshotReader,MonitoringSnapshotReader>();
builder.Services.AddScoped<IMonitoringIncidentQueries,EfMonitoringIncidentQueries>();
builder.Services.AddScoped<MonitoringIncidentWriter>();
builder.Services.AddScoped<MonitoringLive>();
builder.Services.AddSingleton<MonitoringCameraCoordinator>();
builder.Services.AddSingleton<IMonitoringRuntimeState>(s=>s.GetRequiredService<MonitoringCameraCoordinator>());
builder.Services.AddSingleton<IMonitoringSessionOwnership>(s=>s.GetRequiredService<MonitoringCameraCoordinator>());
builder.Services.AddScoped<SetupOverview>();
builder.Services.AddScoped<CameraHealth>();
builder.Services.AddScoped<AiPreview>();
builder.Services.AddHttpClient<IAiPreviewClient, AiPreviewClient>((services, client) =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiPreviewOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddHostedService<CameraHealthWorker>();
builder.Services.AddHttpClient<IAiMonitoringClient,AiMonitoringClient>((services,client)=> {
    var options=services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiPreviewOptions>>().Value;
    client.BaseAddress=new Uri(options.BaseUrl); client.Timeout=TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddHostedService<MonitoringWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new();
    o.TokenValidationParameters = new TokenValidationParameters { ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), ClockSkew = TimeSpan.FromSeconds(30) };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userId, out var id))
            {
                context.Fail("Invalid account.");
                return;
            }
            var store = context.HttpContext.RequestServices.GetRequiredService<ISetupStore>();
            var user = await store.Find<UserAccount>(id, context.HttpContext.RequestAborted);
            var role = user is null ? null : await store.Find<Role>(user.RoleId, context.HttpContext.RequestAborted);
            if (user?.Status != "ACTIVE" || role?.Name != context.Principal?.FindFirstValue(ClaimTypes.Role))
                context.Fail("Account is no longer authorized.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiErrors>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"]).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Frame-Sequence", "X-Session-Id")));
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "FA26SE103 Operations API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement { { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() } });
});
var app = builder.Build();
// Suppress framework exception diagnostics to avoid logging sensitive adapter/SQL messages.
app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "UP" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        return await db.Database.CanConnectAsync(ct) ? Results.Ok(new
        {
            status = "READY"
        }) : Results.StatusCode(503);
    }
    catch (Exception) { return Results.StatusCode(503); }
}).AllowAnonymous();
if (app.Environment.IsDevelopment())
    app.MapPost("/api/demo/cameras/{id:guid}/state", async (Guid id, bool online, DemoCameraState state, CameraSetup setup, CancellationToken ct) =>
{
    var c = await setup.Connection(id, ct);
    if (c.SourceType != "DEMO")
        return Results.Conflict(new
        {
            code = "NOT_DEMO_CAMERA"
        });
    state.Set(id, online);
    return Results.NoContent();
}).RequireAuthorization(p => p.RequireRole("ADMIN"));
await Bootstrap.Run(app);
app.Run();
public partial class Program;
