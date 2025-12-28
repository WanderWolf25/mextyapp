
using MexyApp.Api.Domain;
using MexyApp.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MexyContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default"))
);

var app = builder.Build();

// ÚNICO endpoint: crea usuario con hash BCrypt
app.MapGet("/crear-usuario", async (string username, string email, string password, MexyContext db) =>
{
    var hash = BCrypt.Net.BCrypt.HashPassword(password);
    var user = new User(username, email, hash);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return $"Usuario creado con ID: {user.Id}";
});

app.Run();
