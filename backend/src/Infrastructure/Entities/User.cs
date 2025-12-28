
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

    // Backing field: única fuente de verdad para la relación 1..N
    private readonly List<UserRole> _userRoles = new();

    // Proyección de nombres de rol (solo lectura)
    public IReadOnlyCollection<RoleName> Roles =>
        _userRoles.Select(ur => ur.Role).ToArray();

    private User() { } // EF

    public User(string username, string email, string passwordHash)
    {
        Username = username.Trim();
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;

        // Rol base por defecto
        _userRoles.Add(new UserRole { Role = RoleName.Comprador });
    }

    public bool HasRole(RoleName role) => _userRoles.Any(r => r.Role == role);

    public void AddRole(RoleName role)
    {
        if (HasRole(role)) return; // Evita duplicados
        _userRoles.Add(new UserRole { Role = role });
    }

    public void RemoveRole(RoleName role)
    {
        var link = _userRoles.FirstOrDefault(r => r.Role == role);
        if (link is not null) _userRoles.Remove(link);
    }

    public void Block() => Status = UserStatus.Bloqueado;
}
