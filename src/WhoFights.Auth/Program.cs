using System.Text;
using WhoFights.Auth.Options;
using WhoFights.Auth.Services;
using WhoFights.Data;
using WhoFights.Data.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Neither the automatic user-secrets loading nor reflecting for
    // UserSecretsIdAttribute is reliable under this VS 18 Insiders /
    // .NET 10 preview combo (the FileProvider ends up rooted at the
    // project folder, and GetCustomAttribute returns null even though
    // it's present in the compiled AssemblyInfo). Load the secrets file
    // directly from its fixed OS path instead, using the id straight
    // from WhoFights.Auth.csproj's <UserSecretsId>.
    const string secretsId = "aspnet-WhoFights.Auth-8b3f1c7e-2d4a-4b9e-9f6c-1a5e7d3c8b2f";
    var secretsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", secretsId);
    if (Directory.Exists(secretsDir))
    {
        builder.Configuration.AddJsonFile(new PhysicalFileProvider(secretsDir), "secrets.json", optional: true, reloadOnChange: false);
    }
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));

// Core only, not AddDefaultIdentity: we want UserManager for creating/looking
// up accounts, none of the cookie-based sign-in or Razor UI machinery that
// comes with the "default" package - this service speaks JSON and JWTs only.
// AddSignInManager gives password checks free brute-force lockout tracking
// without using its cookie sign-in; AddDefaultTokenProviders is what makes
// email-confirmation tokens (GenerateEmailConfirmationTokenAsync) work at all.
builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        // Length over complexity rules - current guidance (NIST 800-63B) finds
        // forced digit/case/symbol mixes push people toward predictable
        // substitutions ("Password1!") more than they add real strength.
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 10;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    // The 'required' modifier alone doesn't stop Get<T>() from producing a
    // null here, so this is the actual enforcement - fail loudly at startup
    // instead of a confusing NullReferenceException on the first request.
    throw new InvalidOperationException("Jwt:SigningKey is not configured.");
}
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddScoped<JwtTokenService>();

builder.Services.Configure<GoogleOptions>(builder.Configuration.GetSection(GoogleOptions.SectionName));

var frontendOptions = builder.Configuration.GetSection(FrontendOptions.SectionName).Get<FrontendOptions>()
    ?? throw new InvalidOperationException("Frontend configuration section is missing.");
if (string.IsNullOrWhiteSpace(frontendOptions.BaseUrl))
{
    throw new InvalidOperationException("Frontend:BaseUrl is not configured.");
}
builder.Services.AddSingleton(Options.Create(frontendOptions));

var resendOptions = builder.Configuration.GetSection(ResendOptions.SectionName).Get<ResendOptions>()
    ?? throw new InvalidOperationException("Resend configuration section is missing.");
if (string.IsNullOrWhiteSpace(resendOptions.ApiKey) || string.IsNullOrWhiteSpace(resendOptions.FromAddress))
{
    throw new InvalidOperationException("Resend:ApiKey / Resend:FromAddress is not configured.");
}
builder.Services.AddSingleton(Options.Create(resendOptions));
builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
});

// Any service holding SigningKey can verify a token independently - this is
// what lets the API (and this service's own /auth/me) check "is this user
// really logged in" without a database round trip or a call back here.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim types exactly as issued ("sub", "email", ...) instead
        // of ASP.NET's legacy remap to long http://schemas.xmlsoap.org/...
        // URIs - so the claim names read in controllers match the ones
        // JwtTokenService actually writes.
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
        Title = "WhoFights Auth",
        Version = "v1",
        Description = "Google sign-in and JWT issuance for WhoFights's other services."
    });

    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// Same frontend-CORS pattern as WhoFights.Api - the frontend calls this
// service directly from the browser to sign in.
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

if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseCors(FrontendCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "WhoFights.Auth" }));

app.MapControllers();

app.Run();
