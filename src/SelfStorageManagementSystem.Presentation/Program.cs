using Microsoft.AspNetCore.Mvc;
using SelfStorageManagementSystem.BusinessLogic.Common;
using SelfStorageManagementSystem.BusinessLogic.DependencyInjection;
using SelfStorageManagementSystem.DataAccess.DependencyInjection;
using SelfStorageManagementSystem.Presentation.ExceptionHandling;

var builder = WebApplication.CreateBuilder(args);

// Configure dependency injection layers
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddBusinessLogic();

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

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// HTTP request pipeline
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors(corsPolicyName);

app.UseAuthorization();

app.MapControllers();

app.Run();
