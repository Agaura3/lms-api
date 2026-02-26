using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using System.Threading.RateLimiting;
using StackExchange.Redis;
using Serilog;
using Serilog.Events;
using lms_api.Data;
using lms_api.Middleware;
using lms_api.Hubs;
using lms_api.Services;
using lms_api.BackgroundServices;
using lms_api.Models;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();


// ======================================================
// DATABASE
// ======================================================

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection");

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
// REDIS (OPTIONAL)
// ======================================================

// ======================================================
// REDIS (SAFE OPTIONAL MODE)
// ======================================================

var redisConnection = builder.Configuration["Redis:ConnectionString"];

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var options = ConfigurationOptions.Parse(redisConnection);
        options.AbortOnConnectFail = false;   // 🔥 IMPORTANT
        options.ConnectRetry = 2;
        options.ConnectTimeout = 5000;

        return ConnectionMultiplexer.Connect(options);
    });
}


// ======================================================
// HEALTH CHECKS
// ======================================================

var healthBuilder = builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString);

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    healthBuilder.AddRedis(redisConnection);
}


// ======================================================
// CORS
// ======================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
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

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
                 ?? builder.Configuration["Jwt:Key"];

    var key = Encoding.UTF8.GetBytes(jwtKey!);

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
        IssuerSigningKey = new SymmetricSecurityKey(key)
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
// RBAC
// ======================================================

builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApplyLeave", policy =>
        policy.Requirements.Add(new PermissionRequirement("ApplyLeave")));

    options.AddPolicy("ApproveLeave", policy =>
        policy.Requirements.Add(new PermissionRequirement("ApproveLeave")));

    options.AddPolicy("RejectLeave", policy =>
        policy.Requirements.Add(new PermissionRequirement("RejectLeave")));

    options.AddPolicy("CancelLeave", policy =>
        policy.Requirements.Add(new PermissionRequirement("CancelLeave")));

    options.AddPolicy("ViewDashboard", policy =>
        policy.Requirements.Add(new PermissionRequirement("ViewDashboard")));

    options.AddPolicy("ViewReports", policy =>
        policy.Requirements.Add(new PermissionRequirement("ViewReports")));

    options.AddPolicy("AdminAccess", policy =>
        policy.Requirements.Add(new PermissionRequirement("AdminAccess")));

    options.AddPolicy("ManagementAccess", policy =>
        policy.Requirements.Add(new PermissionRequirement("ManagementAccess")));
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

    options.RejectionStatusCode = 429;
});


// ======================================================
// BUILD
// ======================================================

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionMiddleware>();
app.UseHttpsRedirection();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");

app.UseRateLimiter();
app.UseCors("AllowFrontend");
app.UseAuthentication();

var isSaaS = builder.Configuration.GetValue<bool>("AppMode:IsSaaS");
if (isSaaS)
{
    app.UseMiddleware<SubscriptionMiddleware>();
}

app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();