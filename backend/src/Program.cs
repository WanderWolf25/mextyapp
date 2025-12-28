using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MexyApp.Api.Domain;
using MexyApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using  MexyApp.Api.Endpoints;
var builder = WebApplication.CreateBuilder(args);

// ==========================================
// FLUJO PRINCIPAL (MAIN)
// ==========================================

// 1. Carga la configuración estricta desde appsettings.json
// Limpia proveedores por defecto y asegura que el archivo exista.
ConfigureAppConfiguration(builder);

// 2. Obtiene la cadena de conexión
// Busca 'Default' o parsea 'DefaultUri' (Postgres) si es necesario.
string connString = GetAndParseConnectionString(builder.Configuration, builder.Environment.ContentRootPath);

// 3. Registra los servicios en el contenedor DI
// Configura EF Core con Npgsql, reintentos y el sistema de Logging.
RegisterServices(builder, connString);

// 4. Construye la aplicación
var app = builder.Build();
app.Logger.LogInformation("Inicio de aplicación. Configuración cargada.");

// 5. Mapea los Endpoints (Rutas)
// Define las rutas de la API, como /crear-usuario.
RegisterEndpoints(app);

// 6. Verificación de salud (Health Check manual)
// Intenta conectar a la DB antes de abrir el servidor. Si falla, crashea adrede.
await VerifyDatabaseConnection(app);

// 7. Arranca la aplicación
app.Run();


// ==========================================
// DEFINICIÓN DE FUNCIONES
// ==========================================

/// <summary>
/// Limpia las fuentes de configuración y carga exclusivamente appsettings.json.
/// Valida que el archivo exista físicamente.
/// </summary>
void ConfigureAppConfiguration(WebApplicationBuilder builder)
{
    builder.Configuration.Sources.Clear();
    var appsettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");

    if (!File.Exists(appsettingsPath))
        throw new FileNotFoundException($"No se encontró appsettings.json en: {appsettingsPath}");

    try
    {
        builder.Configuration.AddJsonFile(appsettingsPath, optional: false, reloadOnChange: false);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Error cargando appsettings.json ({appsettingsPath}): {ex.Message}", ex);
    }
}

/// <summary>
/// Resuelve la cadena de conexión final. 
/// Prioriza 'Default'. Si no existe, toma 'DefaultUri' y la convierte a formato Npgsql.
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
/// Inyecta DbContext y configura el Logging.
/// </summary>
void RegisterServices(WebApplicationBuilder builder, string connString)
{
    // Registrar DbContext con reintentos
    builder.Services.AddDbContext<MexyContext>(opt =>
        opt.UseNpgsql(connString, npgsql =>
        {
            npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null);
        })
    );

    // Configurar Logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
}

/// <summary>
/// Define los endpoints de la API.
/// </summary>
void RegisterEndpoints(WebApplication app)
{

app.MapUsersEndpoints();


}

/// <summary>
/// Ejecuta el Ping a la base de datos y lanza excepción si falla.
/// </summary>
async Task VerifyDatabaseConnection(WebApplication app)
{
    var pingOk = await PingDatabaseAsync(app.Services, app.Logger);
    if (pingOk)
        app.Logger.LogInformation("Ping DB: OK");
    else
        throw new InvalidOperationException("Ping DB: ERROR de conexión. Verifica host/puerto/credenciales/SSL en appsettings.json.");
}

// ---------------------------------------------------------
// HELPERS DE BAJO NIVEL (Lógica pura)
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

static string ParsePostgresUriToNpgsql(string uri)
{
    // Normalizar prefijo
    var v = uri.Trim();
    if (!(v.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
          v.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("El URI debe iniciar con 'postgres://' o 'postgresql://'.");
    }

    // Quitar el esquema
    var idxScheme = v.IndexOf("://", StringComparison.Ordinal);
    var rest = v.Substring(idxScheme + 3);

    // userinfo@hostport/db
    var at = rest.IndexOf('@');
    if (at <= 0) throw new InvalidOperationException("URI inválido: falta '@' entre credenciales y host.");

    var userinfo = rest.Substring(0, at);
    var hostAndPath = rest.Substring(at + 1);

    // user:password
    var colon = userinfo.IndexOf(':');
    if (colon <= 0) throw new InvalidOperationException("URI inválido: falta ':' entre usuario y contraseña.");
    var username = userinfo.Substring(0, colon);
    var password = userinfo.Substring(colon + 1); 

    // host:port/db
    var slash = hostAndPath.IndexOf('/');
    if (slash <= 0) throw new InvalidOperationException("URI inválido: falta '/' antes del nombre de la base.");
    var hostport = hostAndPath.Substring(0, slash);
    var database = hostAndPath.Substring(slash + 1);
    if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("URI inválido: nombre de base vacío.");

    // host:port
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

    // Construir cadena Npgsql
    var npgsql = $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    return npgsql;
}