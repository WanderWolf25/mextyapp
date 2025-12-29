
// src/api/Endpoints/UsersEndpoints.cs
using Microsoft.EntityFrameworkCore;
using MexyApp.Api.Contracts;
using MexyApp.Api.Domain;
using Npgsql;
using System.Net.Sockets;

namespace MexyApp.Api.Endpoints;

public static class UsersEndpoints
{
    private static readonly string[] EmailUniqueConstraintNames =
    {
        "IX_Users_Email", "users_email_key", "Users_Email_key"
    };

    private static bool IsUniqueEmailViolation(DbUpdateException ex)
    {
        var pg = ex.InnerException as PostgresException;
        return pg?.SqlState == "23505"
            && !string.IsNullOrEmpty(pg.ConstraintName)
            && (EmailUniqueConstraintNames.Any(n => pg.ConstraintName.Equals(n, StringComparison.OrdinalIgnoreCase))
                || pg.ConstraintName.Contains("email", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTimeout(DbUpdateException ex)
    {
        // Detecta Timeout en la cadena de inner exceptions
        Exception? cur = ex;
        while (cur is not null)
        {
            if (cur is TimeoutException) return true;
            if (cur is NpgsqlException npg && npg.InnerException is TimeoutException) return true;
            if (cur is IOException io && io.InnerException is TimeoutException) return true;
            if (cur is SocketException se && se.SocketErrorCode == SocketError.TimedOut) return true;
            cur = cur.InnerException;
        }
        return false;
    }

    private static string Describe(DbUpdateException ex)
    {
        var pg = ex.InnerException as PostgresException;
        return pg is null
            ? $"Inner={ex.InnerException?.GetType().Name ?? "null"}; Msg={ex.InnerException?.Message ?? "null"}"
            : $"SqlState={pg.SqlState}; Constraint={pg.ConstraintName ?? "null"}; Table={pg.TableName ?? "null"}; Detail={pg.Detail ?? "null"}";
    }

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users");

        group.MapPost("/", async (CreateUserRequest req, MexyContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) ||
                string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest("Username, Email y Password son obligatorios.");

            var email = req.Email.Trim().ToLowerInvariant();

            // Baja el costo mientras diagnosticas (ver sección 3)
            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 4);

            var user = new MexyApp.Models.User(req.Username, email, hash);
            db.Users.Add(user);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueEmailViolation(ex))
            {
                return Results.Conflict($"Email '{email}' ya está registrado.");
            }
            catch (DbUpdateException ex) when (IsTimeout(ex))
            {
                // Falla transitoria (red/latencia). Devuelve 503 para reintento del cliente.
                return Results.Problem(
                    title: "Timeout al persistir en la base de datos",
                    detail: Describe(ex),
                    statusCode: StatusCodes.Status503ServiceUnavailable
                );
            }
            catch (DbUpdateException ex)
            {
                // Otra restricción / error no categorizado
                return Results.Problem(
                    title: "Error al persistir usuario",
                    detail: Describe(ex),
                    statusCode: StatusCodes.Status500InternalServerError
                );
            }

            var dto = new UserResponse(
                user.Id,
                user.Username,
                user.Email,
                user.Status.ToString(),
                user.Roles.Select(r => r.ToString()).ToArray()
            );

            return Results.Created($"/api/users/{user.Id}", dto);
        });

        group.MapGet("/{id:int}", async (int id, MexyContext db, CancellationToken ct) =>
        {
            var user = await db.Users
                .AsNoTracking()
                .Include("_userRoles")
                .FirstOrDefaultAsync(u => u.Id == id, ct);

            if (user is null) return Results.NotFound();

            var dto = new UserResponse(
                user.Id,
                user.Username,
                user.Email,
                user.Status.ToString(),
                user.Roles.Select(r => r.ToString()).ToArray()
            );

            return Results.Ok(dto);
        });

        return routes;
    }
}
