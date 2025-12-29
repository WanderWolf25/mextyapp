
// src/api/Endpoints/UsersEndpoints.cs
using Microsoft.EntityFrameworkCore;
using MexyApp.Api.Contracts;
using MexyApp.Api.Domain;

namespace MexyApp.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users");

        // POST /api/users (validación temprana + unicidad de email)
        group.MapPost("/", async (CreateUserRequest req, MexyContext db) =>
        {
            // Validación de campos obligatorios
            if (string.IsNullOrWhiteSpace(req.Username) ||
                string.IsNullOrWhiteSpace(req.Email) ||
                string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.BadRequest("Username, Email y Password son obligatorios.");
            }

            // Normalización de email
            var email = req.Email.Trim().ToLowerInvariant();

            // Verificación de email duplicado
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict($"Email '{email}' ya está registrado.");

            // Hash de contraseña y creación de entidad
            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 10);
            var user = new MexyApp.Models.User(req.Username, email, hash);

            // Persistencia
            db.Users.Add(user);
            await db.SaveChangesAsync();

            // DTO de respuesta
            var dto = new UserResponse(
                user.Id,
                user.Username,
                user.Email,
                user.Status.ToString(),
                user.Roles.Select(r => r.ToString()).ToArray()
            );

            return Results.Created($"/api/users/{user.Id}", dto);
        });

        // GET /api/users/{id} → carga roles desde el backing field
        group.MapGet("/{id:int}", async (int id, MexyContext db) =>
        {
            var user = await db.Users
                .Include("_userRoles") // colección respaldada por el backing field
                .FirstOrDefaultAsync(u => u.Id == id);

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
