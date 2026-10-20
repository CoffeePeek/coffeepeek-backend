namespace CoffeePeek.Account.Domain.Entities.RoleAggregate;

public interface IRoleRepository
{
    Task<Role?> GetRoleAsync(string roleName);
}