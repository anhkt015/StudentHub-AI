using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StudentHub.API.Data;
using StudentHub.API.Services.Verification;

Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER","1");

var builder = WebApplication.CreateBuilder(args);

// ============================================================
// DATABASE - PostgreSQL / Supabase
// ============================================================

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// ============================================================
// CONTROLLERS
// ============================================================

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ILayer4VerificationService, Layer4VerificationService>();

builder.Services.AddScoped<ILayer2VerificationService, Layer2VerificationService>();
builder.Services.AddScoped<ILayer3VerificationService, Layer3VerificationService>();
// ============================================================
// SUPABASE JWT AUTHENTICATION
// ============================================================

var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase URL is not configured.");

var supabaseJwtIssuer = $"{supabaseUrl.TrimEnd('/')}/auth/v1";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = supabaseJwtIssuer;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = supabaseJwtIssuer,

            ValidateAudience = false,

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,

            NameClaimType = "sub",

            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// ============================================================
// AUTHORIZATION
// ============================================================

builder.Services.AddAuthorization();

// ============================================================
// CORS - FRONTEND VERCEL
// ============================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "https://student-hub-ai-git-develop-anhkt015s-projects.vercel.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// ============================================================
// OPENAPI
// ============================================================

builder.Services.AddOpenApi();

var app = builder.Build();

// ============================================================
// OPENAPI
// ============================================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ============================================================
// MIDDLEWARE
// ============================================================

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
