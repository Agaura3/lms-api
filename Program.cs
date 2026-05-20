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
using LMS.API.Services;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using lms_api.Validators;
using lms_api.Common;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!builder.Environment.IsEnvironment("Testing"))
{
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new Exception("Database connection string is not configured.");

    builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
}

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var message = string.Join("; ",
                context.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));

            return new BadRequestObjectResult(ApiResponse<string>.FailResponse(
                string.IsNullOrWhiteSpace(message) ? "Validation failed" : message));
        };
    });

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString!, name: "PostgreSQL");
}
else
{
    builder.Services.AddHealthChecks();
}

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "LMS API", Version = "v1" });
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
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
             ?? builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
    throw new Exception("JWT key is not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/notificationHub"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

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

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IDashboardBroadcastService, DashboardBroadcastService>();
builder.Services.AddScoped<ILeaveCalculationService, LeaveCalculationService>();
builder.Services.AddScoped<ILoginAttemptService, LoginAttemptService>();
builder.Services.AddScoped<IEmailService, ResendEmailService>();

if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<EmailBackgroundService>();
}
builder.Services.AddHttpClient();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ValidationExceptionMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub").RequireAuthorization();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();

public partial class Program { }
