
public enum RoleName { Comprador, Artesano, Soporte, Administrador }
public enum UserStatus { Activo, Bloqueado } // “Inactivo/Suspendido” suele duplicar Bloqueado

public class User
{
    public int Id { get; private set; }

    public string Username { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;

    public UserStatus Status { get; private set; } = UserStatus.Activo;

    private readonly HashSet<RoleName> _roles = new();/* Esto no es mas que una lista,vector,array o como le quieras llamar.
    la unica diferencia es que, solo se puede agregar elementos una vez de ese mismo valor, si hay un repetido devolvera
    un false, esto es muy util para evitar duplicados  y que guardemos todos los roles que tendra cada usuario.
    el readonly despues de private simplemente significa que no se puede reasignar el tipo a otro, pero si podemos hacer
    add y todo lo que hariamos en una lista normal*/
    public IReadOnlyCollection<RoleName> Roles => _roles;/* creamos una nueva coleccion de solo lectura,
    y el valor que le asignamos es cualquier valor nuevo o viejo que contenga _roles, esto es para que no puedan modificar
    los roles desde afuera de la clase. */

    

    private User() { } // EF
    
// EF
/* Esto es un constructor VACÍO (sin parámetros).
   ¿Para qué sirve? Para que Entity Framework (EF Core) pueda crear un User
   cuando lee datos de la base de datos.

   Piensa en EF como “un cargador de objetos”:
   - EF hace un SELECT a la tabla Users
   - Luego necesita construir un objeto User en memoria
   - Para construirlo, normalmente necesita un constructor sin parámetros
   - Después de crearlo, EF le mete los valores (Id, Email, etc.)

   ¿Por qué es private?
   Porque tú NO quieres que cualquier parte del código pueda hacer:
       new User()
   y dejar el objeto a medias o inválido (sin email, sin roles, etc.)

   Entonces:
   - EF sí lo puede usar (usa reflexión y puede entrar aunque sea private)
   - Tu código normal NO lo puede usar (porque private lo bloquea)
   Resultado: EF puede hidratar la entidad, pero tú controlas cómo se crea “bien”.
*/


    public User(string username, string email, string passwordHash)
    {
        Username = username.Trim();
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        _roles.Add(RoleName.Comprador); // rol base típico
    }

    public bool HasRole(RoleName role) => _roles.Contains(role);

    public void AddRole(RoleName role) => _roles.Add(role);

    public void Block() => Status = UserStatus.Bloqueado;
}
