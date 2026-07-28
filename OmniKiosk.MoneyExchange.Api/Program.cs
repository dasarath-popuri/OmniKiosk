using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Structured logging to console + a rolling daily file. Every request gets
// logged automatically (UseSerilogRequestLogging below), and controllers
// inject ILogger<T> for anything specific (rate lookups, transaction saves).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/moneyexchange-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton(builder.Configuration);

var secretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");

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
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "OmniKiosk.Api",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "OmniKiosk.Wpf",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Same policy names as Config.Api - a token issued by Config.Api's
// /Auth/login carries the same claims regardless of which API validates it,
// since both APIs share the same Jwt:SecretKey/Issuer/Audience.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("KioskOnly", p => p.RequireClaim("MachineType", "Kiosk"))
    .AddPolicy("StaffOnly", p => p.RequireClaim("MachineType", "Staff"))
    .AddPolicy("AdminOrSupervisor", p => p.RequireRole("Administrator", "Supervisor"));

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
}).AddMvc().AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
