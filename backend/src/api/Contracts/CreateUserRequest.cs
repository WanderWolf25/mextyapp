
namespace MexyApp.Api.Contracts;

public sealed record CreateUserRequest(string Username, string Email, string Password);

//Esto es un modelo de datos, nos sirve para saber que datos necesitamos para crear un usuario
//De esta manera mantenemos nuestro codigo limpio y organizado