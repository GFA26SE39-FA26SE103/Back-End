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
using Supermarket.Infrastructure.Persistence;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Supermarket.Infrastructure.Video;
using UserAccount = Supermarket.Domain.UserAccount;
using Role = Supermarket.Domain.Role;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
builder.Services.AddOptions<JwtOptions>().Bind(builder.Configuration.GetSection("Jwt")).Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Set Jwt:Key to a secret containing at least 32 UTF-8 bytes.").Validate(o => o.LifetimeMinutes is > 0 and <= 1440, "JWT lifetime must be 1 to 1440 minutes.").ValidateOnStart();
builder.Services.Configure<VideoOptions>(builder.Configuration.GetSection("Video"));
builder.Services.Configure<HealthWorkerOptions>(builder.Configuration.GetSection("CameraHealth"));
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer") ?? throw new InvalidOperationException("Set ConnectionStrings:SqlServer.")));
var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? Path.Combine(builder.Environment.ContentRootPath, ".local", "keys");
builder.Services.AddDataProtection().SetApplicationName("FA26SE103").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<ICredentialProtector, CredentialProtector>();
builder.Services.AddSingleton<DemoCameraState>();
builder.Services.AddScoped<ICameraStream, CameraStream>();
builder.Services.AddScoped<ISetupStore, EfSetupStore>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITokenIssuer, TokenIssuer>();
builder.Services.AddScoped<Accounts>();
builder.Services.AddScoped<StoreSetup>();
builder.Services.AddScoped<CameraSetup>();
builder.Services.AddScoped<MonitoringSetup>();
builder.Services.AddScoped<CameraHealth>();
builder.Services.AddHostedService<CameraHealthWorker>();
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
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"]).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "FA26SE103 MF-01 API", Version = "v1" });
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
