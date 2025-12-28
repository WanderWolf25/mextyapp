
using Microsoft.EntityFrameworkCore;
using MexyApp.Api.Contracts;
using MexyApp.Api.Domain;

namespace MexyApp.Api.Endpoints;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users").WithTags("Users");

        // POST /api/users (no necesita Include)
        group.MapPost("/", async (CreateUserRequest req, MexyContext db) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email))
                return Results.Conflict($"Email '{email}' ya está registrado.");

            var hash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            var user = new MexyApp.Models.User(req.Username, email, hash);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            var dto = new UserResponse(
                user.Id,
                user.Username,
                user.Email,
                user.Status.ToString(),
                user.Roles.Select(r => r.ToString()).ToArray()
            );

            return Results.Created($"/api/users/{user.Id}", dto);
        });

        // GET /api/users/{id} → aquí va el Include("_userRoles")
        group.MapGet("/{id:int}", async (int id, MexyContext db) =>
        {
            var user = await db.Users
                .Include("_userRoles") // carga la colección respaldada por el backing field
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
