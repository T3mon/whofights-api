using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using WhoFights.Data;
using WhoFights.Data.Options;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Neither the automatic user-secrets loading nor reflecting for
    // UserSecretsIdAttribute is reliable under this VS 18 Insiders /
    // .NET 10 preview combo (the FileProvider ends up rooted at the
    // project folder, and GetCustomAttribute returns null even though
    // it's present in the compiled AssemblyInfo). Load the secrets file
    // directly from its fixed OS path instead, using the id straight
    // from WhoFights.Api.csproj's <UserSecretsId>. Same workaround as
    // WhoFights.Auth/Program.cs, which hit this first.
    const string secretsId = "aspnet-WhoFights.Api-a9fd289b-e2dc-48bb-a17b-225a95fd8593";
    var secretsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", secretsId);
    if (Directory.Exists(secretsDir))
    {
        builder.Configuration.AddJsonFile(new PhysicalFileProvider(secretsDir), "secrets.json", optional: true, reloadOnChange: false);
    }
}

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Validates the JWT that WhoFights.Auth mints - same Issuer/Audience/
// SigningKey on both sides (see JwtOptions), no shared session store or
// call back to Auth needed to check "is this token legit".
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException("Jwt:SigningKey is not configured.");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim types exactly as issued ("sub", "email", ...) instead
        // of ASP.NET's legacy remap to long http://schemas.xmlsoap.org/...
        // URIs, matching WhoFights.Auth's own validation setup.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "WhoFights API",
        Version = "v1",
        Description = "Read-only feed of upcoming combat sports events (UFC, ONE, RIZIN, BKFC, and other tracked "
            + "promotions), scraped from Tapology. No authentication required."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});

// Enums travel as camelCase strings ("weeklyDigest", "email") so the JSON
// reads like the UI and never depends on enum member order.
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddProblemDetails();

// Lets a separately-hosted React frontend (a different origin) call /api/*.
// Origins come from config, not hardcoded, so prod can point at the real
// frontend URL without a code change.
const string FrontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
// Swagger is available on the deployed "dev" environment too (ASPNETCORE_ENVIRONMENT=Staging) -
// that's the whole point of having a dev deployment to poke at. The migrations endpoint and the
// detailed developer exception page stay local-only (true Development), since both would leak
// implementation details or let a stranger apply schema changes if exposed on a public URL.
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();

    // A visual "this isn't Production" cue for whoever's poking at Swagger.
    // Safe by construction, not by a runtime check: this whole code path is
    // absent in Production (see the branch above), so there's no toggle
    // that could leak it to a real user - the banner simply doesn't exist
    // outside Development/Staging.
    var bannerColor = app.Environment.IsStaging() ? "#f2c744" : "#4caf50";
    var bannerLabel = app.Environment.EnvironmentName.ToUpperInvariant();
    app.UseSwaggerUI(options =>
    {
        options.HeadContent =
            $"<div style=\"position:fixed;top:0;left:0;right:0;z-index:9999;background:{bannerColor};" +
            "color:#000;text-align:center;font-weight:bold;padding:6px;font-family:sans-serif;font-size:13px;\">" +
            $"{bannerLabel} — internal use only, never shown to real users</div>" +
            "<style>.swagger-ui { margin-top: 32px; }</style>";
    });
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    // JSON-only API, so exceptions come back as ProblemDetails instead of
    // redirecting to an HTML error page.
    app.UseExceptionHandler();
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

// Hitting the bare domain redirects to the health check instead of a 404 -
// there's no landing page, this is a JSON API for the React frontend.
app.MapGet("/", () => Results.Redirect("/api/health"));

app.MapControllers();

app.Run();
