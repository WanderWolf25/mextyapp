using MexyApp.Data; // Importa el namespace donde está AppDbContext

using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

// Guardamos el color original para restaurarlo después
var colorOriginal = Console.ForegroundColor;

// Cambiamos el color a verde
Console.ForegroundColor = ConsoleColor.Green;

// Escribimos el texto
Console.WriteLine("¡Esto es verde!");

// Restauramos el color original
Console.ForegroundColor = colorOriginal;


Console.WriteLine("Iniciando MexyApp");
// Mensaje en consola para confirmar que la app arranca.

var builder = WebApplication.CreateBuilder(args);
// Crea el builder: configura servicios y la aplicación.

builder.Services.AddControllers();
// Activa soporte para controladores (API REST).

builder.Services.AddOpenApi();
// Activa OpenAPI (Swagger) para documentar la API.

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
// Conecta EF Core a PostgreSQL usando la cadena en appsettings.json.
// AppDbContext es tu clase que representa la base de datos.

var app = builder.Build();
// Construye la aplicación con la configuración anterior.

if (app.Environment.IsDevelopment())
// Si estás en modo desarrollo, activa Swagger.
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
// Redirige HTTP → HTTPS (seguridad).

app.UseAuthorization();
// Middleware para autorización (roles, policies).

app.MapControllers();
// Mapea las rutas de los controladores (ej. /api/users).

// Crea la base y tablas si no existen (modo fácil para MVP).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
// Inicia la aplicación y escucha peticiones.

app.MapGet("/ping-db", async (AppDbContext db) =>
{
    try
    {
        await db.Database.OpenConnectionAsync();
        await db.Database.CloseConnectionAsync();
        return Results.Ok(new { ok = true, message = "Conexión a Supabase exitosa" });
    }
    catch (Exception ex)
    {
        return Results.Problem(title: "Error de conexión", detail: ex.Message, statusCode: 500);
    }
});
