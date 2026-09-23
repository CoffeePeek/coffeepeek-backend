namespace CoffeePeek.Shared.Domain.Interfaces.Infrastructure;

public record CacheKey(
    string Key,
    TimeSpan? DefaultTtl = null,
    string Description = "",
    string Service = "")
{
    public static class User
    {
        public static CacheKey Profile(Guid userId) => new(
            Key: $"user:profile:{userId}",
            DefaultTtl: TimeSpan.FromHours(1),
            Description: "User profile DTO",
            Service: "UserService");
            
        public static CacheKey Entity(Guid userId) => new(
            Key: $"user:entity:{userId}",
            DefaultTtl: TimeSpan.FromHours(1),
            Description: "User entity",
            Service: "UserService");
            
        public static CacheKey ByEmail(string email) => new(
            Key: $"user:email:{email.ToLowerInvariant()}",
            DefaultTtl: TimeSpan.FromHours(1),
            Description: "User by email",
            Service: "UserService");
        
            
        
        public static string AllPattern() => "user:*";
    }
    
    public static class Auth
    {
        public static CacheKey Credentials(Guid userId) => new(
            Key: $"auth:credentials:{userId}",
            DefaultTtl: TimeSpan.FromMinutes(30),
            Description: "User credentials entity",
            Service: "AuthService");
            
        public static CacheKey CredentialsByEmail(string email) => new(
            Key: $"auth:credentials:email:{email.ToLowerInvariant()}",
            DefaultTtl: TimeSpan.FromMinutes(5),
            Description: "User credentials by email",
            Service: "AuthService");
            
        public static CacheKey RefreshToken(string token) => new(
            Key: $"auth:refresh-token:{token}",
            DefaultTtl: TimeSpan.FromDays(1),
            Description: "Refresh token",
            Service: "AuthService");
            
        public static string CredentialsPattern() => "auth:credentials:*";
        
        public static string AllPattern() => "auth:*";
    }
    
    public static class Shop
    {
        public static CacheKey Detail(Guid shopId) => new(
            Key: $"shop:detail:v3:{shopId}",
            DefaultTtl: TimeSpan.FromMinutes(3),
            Description: "Coffee shop entity (details)",
            Service: "ShopsService");
        
        public static string DetailPattern() => "shop:detail:*";
        
        public static CacheKey ListByCity(Guid cityId, int pageNumber, int pageSize) => new(
            Key: $"shop:list:city:{cityId}:page:{pageNumber}:size:{pageSize}",
            DefaultTtl: TimeSpan.FromMinutes(5),
            Description: "Coffee shops list by city with paging",
            Service: "ShopsService");
        
        public static string ListByCityPattern(Guid cityId) => $"shop:list:city:{cityId}:*";

        public static string ListPattern() => "shop:list:city:*";
            
        public static CacheKey Visited(Guid userId) => new(
            Key: $"shop:visited:{userId}",
            DefaultTtl: TimeSpan.FromHours(1),
            Description: "User visited shops",
            Service: "ShopsService");
        
        public static CacheKey VisitStatistics(Guid userId) => new(
            Key: $"shop:visit-stats:{userId}",
            DefaultTtl: TimeSpan.FromHours(1),
            Description: "User visit statistics",
            Service: "ShopsService");
        
        public static CacheKey Search(string searchHash) => new(
            Key: $"shop:search:v3:{searchHash}",
            DefaultTtl: TimeSpan.FromMinutes(5),
            Description: "Shop search results",
            Service: "ShopsService");
        
        public static CacheKey PublicPlatformStats() => new(
            Key: "shop:public:platform-stats",
            DefaultTtl: TimeSpan.FromMinutes(5),
            Description: "Public landing page platform statistics",
            Service: "ShopsService");
        
        public static string SearchPattern() => "shop:search:*";

        public static CacheKey TagsCatalog() => new(
            Key: "shop:tags:catalog",
            DefaultTtl: TimeSpan.FromHours(1),
            Description: "Active shop filter tags catalog",
            Service: "ShopsService");

        public static string TagsCatalogPattern() => "shop:tags:*";

        public static CacheKey MenuDrinks() => new(
            Key: "shop:menu:drinks",
            DefaultTtl: TimeSpan.FromHours(6),
            Description: "Standard coffee drink catalog",
            Service: "ShopsService");
    }
    
    public static class City
    {
        public static CacheKey ListAll() => new(
            Key: "city:list:all",
            DefaultTtl: TimeSpan.FromDays(1),
            Description: "All cities list",
            Service: "ShopsService");
        
        public static string ListPattern() => "city:list:*";
    }
    
    public static class Equipment
    {
        public static CacheKey ListAll() => new(
            Key: "equipment:list:all",
            DefaultTtl: TimeSpan.FromDays(1),
            Description: "All equipment list",
            Service: "ShopsService");
        
        public static string ListPattern() => "equipment:list:*";
    }
    
    public static class CoffeeBean
    {
        public static CacheKey ListAll() => new(
            Key: "coffeebean:list:all",
            DefaultTtl: TimeSpan.FromDays(1),
            Description: "All coffee beans list",
            Service: "ShopsService");
        
        public static string ListPattern() => "coffeebean:list:*";
    }
    
    public static class Roaster
    {
        public static CacheKey ListAll() => new(
            Key: "roaster:list:all",
            DefaultTtl: TimeSpan.FromDays(1),
            Description: "All roasters list",
            Service: "ShopsService");
        
        public static string ListPattern() => "roaster:list:*";
    }
    
    public static class BrewMethod
    {
        public static CacheKey ListAll() => new(
            Key: "brewmethod:list:all",
            DefaultTtl: TimeSpan.FromDays(1),
            Description: "All brew methods list",
            Service: "ShopsService");
        
        public static string ListPattern() => "brewmethod:list:*";
    }
    
    public static string AllPattern() => "*";
    
    /// <summary>
    /// Cache categories for admin invalidation
    /// </summary>
    public static class Categories
    {
        public static class Account
        {
            public const string Users = "users";
            public const string Auth = "auth";
            
            private static readonly Dictionary<string, string> Descriptions = new()
            {
                [Users] = "User profiles and entities",
                [Auth] = "Authentication data (credentials, tokens)"
            };
            
            private static readonly Dictionary<string, string[]> Patterns = new()
            {
                [Users] = [User.AllPattern()],
                [Auth] = [CacheKey.Auth.AllPattern()]
            };
            
            public static Dictionary<string, string> GetDescriptions() => new(Descriptions);
            public static Dictionary<string, string[]> GetPatterns() => new(Patterns);
        }
        
        public static class Shops
        {
            public const string Dictionaries = "dictionaries";
            public const string Lists = "shops-lists";
            public const string Details = "shops-details";
            public const string Search = "shops-search";
            
            private static readonly Dictionary<string, string> Descriptions = new()
            {
                [Dictionaries] = "Cities, Equipment, Beans, Roasters, Brew Methods",
                [Lists] = "Coffee shops lists by city (paginated)",
                [Details] = "Shop details",
                [Search] = "Coffee shops search results"
            };
            
            private static readonly Dictionary<string, string[]> Patterns = new()
            {
                [Dictionaries] = [City.ListPattern(), Equipment.ListPattern(), CoffeeBean.ListPattern(), 
                                   Roaster.ListPattern(), BrewMethod.ListPattern(), Shop.TagsCatalogPattern()],
                [Lists] = [Shop.ListPattern()],
                [Details] = [Shop.DetailPattern()],
                [Search] = [Shop.SearchPattern()]
            };
        }
    }
}
