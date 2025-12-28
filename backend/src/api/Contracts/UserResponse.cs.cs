
namespace MexyApp.Api.Contracts;

public sealed record UserResponse(
    int Id,
    string Username,
    string Email,
    string Status,
    string[] Roles
);
//Este es un modelo de datos que utilizamos para devolver la informacion del usuario
//Despues de que se ha creado o consultado un usuario   