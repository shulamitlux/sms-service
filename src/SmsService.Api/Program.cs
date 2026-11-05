using Polly;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Data;
using SmsService.Api.Endpoints;
using SmsService.Api.Sending;

var builder = WebApplication.CreateBuilder(args);

// The password is kept out of the connection string (and out of the repo): it comes from
// Database:Password (env var Database__Password in Docker, user-secrets in development).
var connectionString = new SqlConnectionStringBuilder(
    builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing connection string 'ConnectionStrings:Default' (see README)."));
if (builder.Configuration["Database:Password"] is { Length: > 0 } dbPassword)
    connectionString.Password = dbPassword;

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString.ConnectionString));
builder.Services.AddScoped<EmployeeSeeder>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SendWorkerSignal>();
builder.Services.AddHostedService<SendWorker>();

var providerBaseUrl = builder.Configuration["SmsProvider:BaseUrl"]
    ?? throw new InvalidOperationException("Missing 'SmsProvider:BaseUrl' (env var SmsProvider__BaseUrl).");
builder.Services.AddHttpClient<ISmsProviderClient, SmsProviderClient>(c =>
        c.BaseAddress = new Uri(providerBaseUrl.TrimEnd('/') + "/"))
    // Timeout only. No retry here: retries are owned by SendWorker and persisted in Attempts.
    .AddResilienceHandler("sms-provider", pipeline => pipeline.AddTimeout(TimeSpan.FromSeconds(5)));

builder.Services.AddProblemDetails(); // RFC 9457 responses, including traceId
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler(); // unhandled exceptions -> ProblemDetails, no stack trace to the client
app.UseStatusCodePages();

// Swagger is enabled in every environment: the service only runs locally (no deploy).
app.MapOpenApi();
app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "SMS Service v1"));

app.MapMessagesEndpoints();
app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "Healthy" })
            : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable"))
    .WithTags("Health");

await InitializeDatabaseAsync(app);

app.Run();

// Migrations + seed on startup, so `docker compose up` needs no manual steps.
static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    var seedFile = Path.Combine(AppContext.BaseDirectory, "Data", "employees.seed.json");
    if (File.Exists(seedFile))
        await scope.ServiceProvider.GetRequiredService<EmployeeSeeder>().SeedAsync(EmployeeSeeder.LoadFromFile(seedFile));
}

public partial class Program; // visible to WebApplicationFactory in the tests
