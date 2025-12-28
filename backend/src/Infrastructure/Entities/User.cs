
namespace MexyApp.Models;

public enum RoleName { Comprador, Artesano, Soporte, Administrador }
public enum UserStatus { Activo, Bloqueado }

public class User
{
    public int Id { get; private set; }

    public string Username { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserStatus Status { get; private set; } = UserStatus.Activo;

    // Navegación persistida (source of truth)
    public ICollection<UserRole> UserRoles { get; } = new List<UserRole>();

    private User() { } // EF

    public User(string username, string email, string passwordHash)
    {
        Username = username.Trim();
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;

        // Rol base típico; usa la navegación para que EF persista correctamente
        UserRoles.Add(new UserRole { Role = RoleName.Comprador, User = this });
    }

    public void SetPassword(string hash) => PasswordHash = hash;

    public bool HasRole(RoleName role) => UserRoles.Any(r => r.Role == role);

    public void AddRole(RoleName role)
    {
        if (!HasRole(role))
            // Vincula por navegación; EF resolverá la FK al guardar
            UserRoles.Add(new UserRole { Role = role, User = this });
    }

    public void RemoveRole(RoleName role)
    {
        var ur = UserRoles.FirstOrDefault(r => r.Role == role);
        if (ur is not null) UserRoles.Remove(ur);
    }

    public void Block() => Status = UserStatus.Bloqueado;
}
