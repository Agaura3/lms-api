using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using Serilog.Events;
using lms_api.Data;
using lms_api.Middleware;
using lms_api.Hubs;
using lms_api.Services;
using lms_api.BackgroundServices;
using lms_api.Models;
using System.Text.Json.Serialization;
using StackExchange.Redis;

Console.WriteLine("STEP 1: Starting application");

// LOGGING CONFIGURATION
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

Console.WriteLine("STEP 2: Logger configured");

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

Console.WriteLine("STEP 3: Builder created");

// DATABASE CONFIGURATION
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

Console.WriteLine("STEP 4: Connection string loaded");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new Exception("❌ Database connection string is not configured.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

Console.WriteLine("STEP 5: DbContext configured");

// REDIS CONFIGURATION
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(configuration);
});

Console.WriteLine("STEP 5.1: Redis configured");

// CONTROLLERS
builder.Services.AddControllers()
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

Console.WriteLine("STEP 6: Controllers added");

// SIGNALR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();
Console.WriteLine("STEP 7: SignalR configured");

// HEALTH CHECKS
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "PostgreSQL");

Console.WriteLine("STEP 8: HealthChecks configured");

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                "https://lms-ui-e5hz.vercel.app",
                "http://localhost:4200"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

Console.WriteLine("STEP 9: CORS configured");

// SWAGGER
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LMS API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

Console.WriteLine("STEP 10: Swagger configured");

// JWT AUTHENTICATION
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
             ?? builder.Configuration["Jwt:Key"];

Console.WriteLine("STEP 11: JWT key loaded");

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new Exception(" JWT key is not configured.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

Console.WriteLine("STEP 12: JWT configured");

// AUTHORIZATION
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApplyLeave", p => p.Requirements.Add(new PermissionRequirement("ApplyLeave")));
    options.AddPolicy("ApproveLeave", p => p.Requirements.Add(new PermissionRequirement("ApproveLeave")));
    options.AddPolicy("RejectLeave", p => p.Requirements.Add(new PermissionRequirement("RejectLeave")));
    options.AddPolicy("CancelLeave", p => p.Requirements.Add(new PermissionRequirement("CancelLeave")));
    options.AddPolicy("ViewDashboard", p => p.Requirements.Add(new PermissionRequirement("ViewDashboard")));
    options.AddPolicy("ViewReports", p => p.Requirements.Add(new PermissionRequirement("ViewReports")));
    options.AddPolicy("AdminAccess", p => p.Requirements.Add(new PermissionRequirement("AdminAccess")));
    options.AddPolicy("ManagementAccess", p => p.Requirements.Add(new PermissionRequirement("ManagementAccess")));
});

Console.WriteLine("STEP 13: Authorization configured");

// EMAIL BACKGROUND SERVICE
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddHostedService<EmailBackgroundService>();

Console.WriteLine("STEP 14: Email service configured");

// RATE LIMITING
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("global", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });

    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

Console.WriteLine("STEP 15: Rate limiter configured");

// BUILD APP
var app = builder.Build();

Console.WriteLine("STEP 16: App built");

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();

app.UseRateLimiter();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

Console.WriteLine("STEP 17: Endpoints mapped");

// AUTO MIGRATION
using (var scope = app.Services.CreateScope())
{
    Console.WriteLine("STEP 18: Starting database migration");

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    Console.WriteLine("STEP 19: Database migration completed");
}

Console.WriteLine("STEP 20: Application starting...");

app.Run();