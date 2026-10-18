namespace CoffeePeek.MediaService.Data;

public sealed class PhotoVariant
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid PhotoId { get; set; }
    public string SourceVersion { get; set; } = "";
    public int RecipeVersion { get; set; }
    public string Purpose { get; set; } = "";
    public string Format { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string Digest { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
}
