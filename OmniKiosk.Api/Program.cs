using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var secretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is not configured. Set it in appsettings.json or an environment variable.");

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

// Same policy names as MoneyExchange.Api - a token issued here carries the
// same claims regardless of which API validates it, since both share the
// same Jwt:SecretKey/Issuer/Audience.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("KioskOnly", p => p.RequireClaim("MachineType", "Kiosk"))
    .AddPolicy("StaffOnly", p => p.RequireClaim("MachineType", "Staff"))
    .AddPolicy("AdminOrSupervisor", p => p.RequireRole("Administrator", "Supervisor"));

builder.Services.AddMemoryCache();
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

//app.UseHttpsRedirection();

app.UseAuthentication(); // Verifies who you are (Reads the JWT)
app.UseAuthorization();  // Verifies what you are allowed to do

app.MapControllers();
app.Run();
