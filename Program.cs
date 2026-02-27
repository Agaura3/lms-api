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

// ======================================================
// LOGGING CONFIGURATION
// ======================================================

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ======================================================
// DATABASE CONFIGURATION
// ======================================================

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new Exception("❌ Database connection string is not configured.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// ======================================================
// CONTROLLERS
// ======================================================

builder.Services.AddControllers();

// ======================================================
// SIGNALR
// ======================================================

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

// ======================================================
// HEALTH CHECKS
// ======================================================

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "PostgreSQL");

// ======================================================
// CORS
// ======================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("https://lms-ui-e5hz.vercel.app")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
// ======================================================
// SWAGGER
// ======================================================

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

// ======================================================
// JWT AUTHENTICATION
// ======================================================

var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
             ?? builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new Exception("❌ JWT key is not configured.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // 🔥 Important for Render
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

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments("/notificationHub"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// ======================================================
// AUTHORIZATION (RBAC)
// ======================================================

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

// ======================================================
// EMAIL BACKGROUND SERVICE
// ======================================================

builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddHostedService<EmailBackgroundService>();

// ======================================================
// RATE LIMITING
// ======================================================

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

// ======================================================
// BUILD APP
// ======================================================

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();

// ⚠️ Render handles HTTPS externally
// So avoid forcing HTTPS internally
// app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

// ======================================================
// AUTO MIGRATION (SAFE)
// ======================================================

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();