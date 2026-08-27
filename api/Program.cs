using System.Text;
using api.Data;
using api.Hubs;
using api.Options;
using api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

// Must exist before WebApplication.CreateBuilder resolves the static-file provider below —
// creating it later (e.g. lazily in LocalFileStorageService) is too late on a fresh clone,
// since a missing wwwroot at startup gets baked in as a null file provider.
Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads"));

var builder = WebApplication.CreateBuilder(args);

const string AngularClientCorsPolicy = "AngularClient";

// Railway (and most PaaS hosts) assign the container's listening port via $PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT token returned by /api/auth/login or /api/auth/signup.",
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), new List<string>() },
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "No database connection configured. Set ConnectionStrings:DefaultConnection via " +
        "'dotnet user-secrets' locally, or the ConnectionStrings__DefaultConnection environment variable in production.");

builder.Services.AddDbContext<ChatDbContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddSignalR();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<CryptoOptions>(builder.Configuration.GetSection(CryptoOptions.SectionName));
builder.Services.AddSingleton<CryptoHelper>();
builder.Services.AddSingleton<ImageCompressionService>();
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "Jwt:Key is not configured. Set it via 'dotnet user-secrets set \"Jwt:Key\" \"<base64-32-bytes>\"' locally, or the Jwt__Key environment variable in production.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim types as issued ("sub", "name", "email") instead of the legacy
        // ClaimTypes.* URIs the handler remaps them to by default.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Browsers can't set an Authorization header on the WebSocket handshake, so the
        // SignalR JS client sends the token as ?access_token=... instead. Only honor that
        // for the hub path — everything else must still use the Authorization header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// In production the Angular build is served from this same app (see MapFallbackToFile below),
// so cross-origin requests normally don't happen — this only matters for local `ng serve` dev,
// or if the frontend is ever split into its own deployment. Override via the AllowedOrigin
// environment variable rather than hardcoding a second origin here.
var allowedOrigin = builder.Configuration["AllowedOrigin"] ?? "http://localhost:4200";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularClientCorsPolicy, policy =>
    {
        policy
            .WithOrigins(allowedOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Railway (and most PaaS hosts) terminate TLS at their edge and forward plain HTTP to the
// container, adding X-Forwarded-* headers. Without this, UseHttpsRedirection() below would
// see every request as HTTP and could redirect-loop.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownIPNetworks = { },
    KnownProxies = { },
});

using (var migrationScope = app.Services.CreateScope())
{
    var migrationDb = migrationScope.ServiceProvider.GetRequiredService<ChatDbContext>();
    await migrationDb.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();

    using var seedScope = app.Services.CreateScope();
    var seedDb = seedScope.ServiceProvider.GetRequiredService<ChatDbContext>();
    var seedCrypto = seedScope.ServiceProvider.GetRequiredService<CryptoHelper>();
    await DbSeeder.SeedAsync(seedDb, seedCrypto);
}

app.UseHttpsRedirection();

// Uploaded files are only ever real, re-encoded images or documents from the extension
// allowlist (see FilesController) — nosniff is defense-in-depth against a browser
// second-guessing the Content-Type and rendering something as HTML.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    },
});

app.UseCors(AngularClientCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat");

// A request under /api or /hubs that didn't match a real endpoint above is a genuine 404,
// not a client-side route — without these, MapFallbackToFile below would serve index.html
// for e.g. a typo'd API path instead of a proper 404.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallback("/hubs/{**path}", () => Results.NotFound());

// Serves the Angular build (copied into wwwroot at Docker build time — see Dockerfile) and
// lets client-side routes like /chat survive a hard refresh instead of 404ing.
app.MapFallbackToFile("index.html");

app.Run();
