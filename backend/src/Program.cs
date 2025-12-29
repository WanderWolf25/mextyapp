
// src/Program.cs
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MexyApp.Api.Domain;
using MexyApp.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// FLUJO PRINCIPAL (MAIN)
// ==========================================

// 1) Cargar configuración desde appsettings.json (sin usar connString aquí)
ConfigureAppConfiguration(builder);

// 2) Obtener cadena de conexión final
string connString = GetAndParseConnectionString(builder.Configuration, builder.Environment.ContentRootPath);

// 3) Registrar servicios (usa connString)
RegisterServices(builder, connString);

// 4) Construir la app
var app = builder.Build();
app.Logger.LogInformation("Inicio de aplicación. Configuración cargada.");

// 5) Mapear endpoints
RegisterEndpoints(app);

// 6) Verificación de salud previa al arranque (crashea si no conecta)
await VerifyDatabaseConnection(app);

// 7) Arrancar
app.Run();


// ==========================================
// FUNCIONES
// ==========================================

/// <summary>
/// Limpia las fuentes y carga exclusivamente appsettings.json.
/// </summary>
void ConfigureAppConfiguration(WebApplicationBuilder builder)
{
    builder.Configuration.Sources.Clear();

    var appsettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
    if (!File.Exists(appsettingsPath))
        throw new FileNotFoundException($"No se encontró appsettings.json en: {appsettingsPath}");

    builder.Configuration.AddJsonFile(appsettingsPath, optional: false, reloadOnChange: false);
}

/// <summary>
/// Resuelve la cadena de conexión. Prioriza 'ConnectionStrings:Default'.
/// Si no existe, parsea 'ConnectionStrings:DefaultUri' (URI Postgres → Npgsql).
/// </summary>
string GetAndParseConnectionString(ConfigurationManager configuration, string rootPath)
{
    string? connString = configuration.GetConnectionString("Default");

    if (string.IsNullOrWhiteSpace(connString))
    {
        var uri = configuration["ConnectionStrings:DefaultUri"];
        if (string.IsNullOrWhiteSpace(uri))
            throw new InvalidOperationException(
                $"Falta ConnectionStrings:Default o ConnectionStrings:DefaultUri en appsettings.json en {rootPath}."
            );

        connString = ParsePostgresUriToNpgsql(uri);
    }

    return connString!;
}

/// <summary>
/// Registra DbContext (pool) y Logging. Sin reintentos mientras diagnosticas.
/// </summary>
void RegisterServices(WebApplicationBuilder builder, string connString)
{
    builder.Services.AddDbContextPool<MexyContext>(opt =>
    {
        opt.UseNpgsql(connString, npgsql =>
        {
            // Evita “permas” en comandos largos bajo latencia
            npgsql.CommandTimeout(10);

            // Evita batches grandes (inserta Users y UserRoles sin agrupar)
            npgsql.MaxBatchSize(1);

            // Actívalo luego de estabilizar si quieres reintentos:
            // npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null);
        });

        // Más detalle en dev para diagnosticar
        if (builder.Environment.IsDevelopment())
        {
            opt.EnableDetailedErrors();
            opt.EnableSensitiveDataLogging();
        }
    });

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    if (builder.Environment.IsDevelopment())
        builder.Logging.AddDebug();
}

/// <summary>
/// Define los endpoints de la API.
/// </summary>
void RegisterEndpoints(WebApplication app)
{
    app.MapUsersEndpoints();
}

/// <summary>
/// Ping a la DB antes de abrir el servidor.
/// </summary>
async Task VerifyDatabaseConnection(WebApplication app)
{
    var ok = await PingDatabaseAsync(app.Services, app.Logger);
    if (ok)
        app.Logger.LogInformation("Ping DB: OK");
    else
        throw new InvalidOperationException("Ping DB: ERROR de conexión. Verifica host/puerto/credenciales/SSL en appsettings.json.");
}

// ---------------------------------------------------------
// HELPERS
// ---------------------------------------------------------

static async Task<bool> PingDatabaseAsync(IServiceProvider services, ILogger logger)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MexyContext>();

    try
    {
        var can = await db.Database.CanConnectAsync();
        if (!can)
            logger.LogError("CanConnectAsync devolvió false (verifica host/puerto/credenciales/SSL).");
        return can;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Excepción durante Ping a la DB");
        return false;
    }
}

/// <summary>
/// Convierte un URI Postgres (postgres://user:pass@host:port/db) a cadena Npgsql.
/// </summary>
static string ParsePostgresUriToNpgsql(string uri)
{
    var v = uri.Trim();
    if (!(v.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
          v.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("El URI debe iniciar con 'postgres://' o 'postgresql://'.");

    var idxScheme = v.IndexOf("://", StringComparison.Ordinal);
    var rest = v.Substring(idxScheme + 3);

    var at = rest.IndexOf('@');
    if (at <= 0) throw new InvalidOperationException("URI inválido: falta '@' entre credenciales y host.");

    var userinfo = rest.Substring(0, at);
    var hostAndPath = rest.Substring(at + 1);

    var colon = userinfo.IndexOf(':');
    if (colon <= 0) throw new InvalidOperationException("URI inválido: falta ':' entre usuario y contraseña.");
    var username = userinfo.Substring(0, colon);
    var password = userinfo.Substring(colon + 1);

    var slash = hostAndPath.IndexOf('/');
    if (slash <= 0) throw new InvalidOperationException("URI inválido: falta '/' antes del nombre de la base.");
    var hostport = hostAndPath.Substring(0, slash);
    var database = hostAndPath.Substring(slash + 1);
    if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("URI inválido: nombre de base vacío.");

    string host;
    int port = 5432;
    var colonHp = hostport.LastIndexOf(':');
    if (colonHp > 0)
    {
        host = hostport.Substring(0, colonHp);
        var portStr = hostport.Substring(colonHp + 1);
        if (!int.TryParse(portStr, out port))
            throw new InvalidOperationException($"Puerto inválido en URI: '{portStr}'.");
    }
    else
    {
        host = hostport;
    }

    var npgsql =
        $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    return npgsql;
}
