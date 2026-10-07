using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.BusinessLogic.Services.Interfaces;
using SelfStorageManagementSystem.DataAccess.Context;
using SelfStorageManagementSystem.DataAccess.DependencyInjection;
using SelfStorageManagementSystem.Presentation.ExceptionHandling;
using SelfStorageManagementSystem.Presentation.Workers;

var builder = WebApplication.CreateBuilder(args);

// Configure dependency injection layers
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddBusinessLogic();

// Background worker for reservation hold expiration (BR-RSV-01)
builder.Services.AddHostedService<ReservationHoldExpirationWorker>();

// Centralized exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Uniform API model validation response formatting
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e =>
                    string.IsNullOrWhiteSpace(e.ErrorMessage)
                        ? "The supplied value is invalid."
                        : e.ErrorMessage).ToArray()
            );

        var response = new ApiResponse<object?>
        {
            Success = false,
            Message = "Validation failed.",
            Data = null,
            Errors = errors
        };

        return new BadRequestObjectResult(response);
    };
});

// Centralized JWT configuration validation and registration
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY");

if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < JwtOptions.MinimumKeyLengthBytes)
{
    throw new InvalidOperationException(
        $"JWT signing key is missing or shorter than {JwtOptions.MinimumKeyLengthBytes} bytes (256 bits). " +
        "Please configure a valid key using User Secrets ('dotnet user-secrets set \"Jwt:Key\" \"<your-secret>\"') " +
        "or the 'JWT_SECRET_KEY' environment variable.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SelfStoragePRN222";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "SelfStoragePRN222Clients";
var expiryMinutesStr = builder.Configuration["Jwt:ExpiryMinutes"];
var expiryMinutes = int.TryParse(expiryMinutesStr, out var m) && m > 0 ? m : 60;

var jwtOptions = new JwtOptions
{
    Key = jwtKey,
    Issuer = jwtIssuer,
    Audience = jwtAudience,
    ExpiryMinutes = expiryMinutes
};

builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(jwtOptions));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(userIdClaim, out var userId))
            {
                context.Fail("Invalid user token claim.");
                return;
            }

            var dbContext = context.HttpContext.RequestServices.GetRequiredService<SelfStorageDbContext>();
            var user = await dbContext.users
                .Include(u => u.user_roleusers)
                    .ThenInclude(ur => ur.role)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.id == userId);

            if (user == null || user.status != "active")
            {
                context.Fail("User is inactive or locked.");
                return;
            }

            // Dynamically revalidate and synchronize roles in ClaimsPrincipal with active DB roles
            if (context.Principal?.Identity is ClaimsIdentity appIdentity)
            {
                var existingRoleClaims = appIdentity.FindAll(ClaimTypes.Role).ToList();
                foreach (var claim in existingRoleClaims)
                {
                    appIdentity.RemoveClaim(claim);
                }

                foreach (var ur in user.user_roleusers)
                {
                    if (!string.IsNullOrWhiteSpace(ur.role?.code))
                    {
                        appIdentity.AddClaim(new Claim(ClaimTypes.Role, ur.role.code));
                    }
                }
            }
        }
    };
});

builder.Services.AddAuthorization();

// Configuration-driven CORS
const string corsPolicyName = "FrontendPolicy";
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
    {
        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// Swagger/OpenAPI with JWT Bearer scheme
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SelfStorage Management API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Bearer token. Enter 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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

var app = builder.Build();

// HTTP request pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    var demoAccountsEnabled = app.Configuration.GetValue<bool>("DemoAccounts:Enabled");
    if (demoAccountsEnabled)
    {
        // Safely bootstrap demo accounts with valid password hashes if needed
        using var scope = app.Services.CreateScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IDemoAccountBootstrapService>();
        await bootstrapper.BootstrapDemoAccountsAsync();
    }
}

app.UseHttpsRedirection();

app.UseCors(corsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }

