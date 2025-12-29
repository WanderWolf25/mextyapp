
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
            npgsql.CommandTimeout(25);
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null);

             // MaxBatchSize(1) -> retirar (mantén fuera salvo diagnóstico puntual)

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
    // 1. Limpieza básica
    var v = uri.Trim();
    if (v.StartsWith("postgres://")) v = v.Substring("postgres://".Length);
    else if (v.StartsWith("postgresql://")) v = v.Substring("postgresql://".Length);
    else throw new InvalidOperationException("El URI debe iniciar con 'postgres://' o 'postgresql://'.");

    // 2. Separar credenciales (@)
    var atIndex = v.LastIndexOf('@'); // Usamos LastIndexOf por si la contraseña tiene '@'
    if (atIndex <= 0) throw new InvalidOperationException("No se encontró '@' separando credenciales del host.");

    var userInfo = v.Substring(0, atIndex);
    var hostData = v.Substring(atIndex + 1);

    // 3. Separar Usuario y Contraseña (:)
    var colonIndex = userInfo.IndexOf(':');
    if (colonIndex <= 0) throw new InvalidOperationException("No se encontró ':' entre usuario y contraseña.");
    
    var username = userInfo.Substring(0, colonIndex);
    var password = userInfo.Substring(colonIndex + 1);

    // 4. Separar Host y Base de Datos (/)
    var slashIndex = hostData.IndexOf('/');
    if (slashIndex <= 0) throw new InvalidOperationException("No se encontró '/' antes de la base de datos.");

    var hostPort = hostData.Substring(0, slashIndex);
    var dbAndParams = hostData.Substring(slashIndex + 1);

    // 5. Separar Nombre de DB y Parámetros (?)
    string database;
    string paramsString = "";

    var questionMarkIndex = dbAndParams.IndexOf('?');
    if (questionMarkIndex >= 0)
    {
        database = dbAndParams.Substring(0, questionMarkIndex);
        // Convertimos el formato URL (&) al formato Npgsql (;)
        paramsString = dbAndParams.Substring(questionMarkIndex + 1).Replace('&', ';');
    }
    else
    {
        database = dbAndParams;
    }

    // 6. Manejo del Puerto
    string host = hostPort;
    string port = "5432";
    
    var portColon = hostPort.LastIndexOf(':');
    if (portColon >= 0)
    {
        host = hostPort.Substring(0, portColon);
        port = hostPort.Substring(portColon + 1);
    }

    // 7. Construir cadena final
    // Nota: Agregamos paramsString al final para que KeepAlive, SSL, etc. se apliquen
    return $"Host={host};Port={port};Database={database};Username={username};Password={password};{paramsString}";
}