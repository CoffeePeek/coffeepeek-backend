namespace CoffeePeek.Shops.Persistance.Community;

/// <summary>Community-owned read projection, independent of the check-in content revision.</summary>
public sealed class CheckInPhotoVariantManifest
{
    public Guid PhotoId { get; set; }
    public Guid CheckInId { get; set; }
    public string SourceVersion { get; set; } = "";
    public int RecipeVersion { get; set; }
    public string VariantsJson { get; set; } = "[]";
}
