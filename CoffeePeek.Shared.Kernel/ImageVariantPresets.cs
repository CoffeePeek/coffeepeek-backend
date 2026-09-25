namespace CoffeePeek.Shared.Kernel;

/// <summary>
/// imgproxy preset names used to build resized image variant URLs.
/// These names MUST match the presets declared in <c>IMGPROXY_PRESETS</c> in <c>deploy/docker-compose.yml</c>:
/// imgproxy runs in presets-only mode and rejects any preset it does not know.
/// </summary>
public static class ImageVariantPresets
{
    /// <summary>Small list/grid thumbnail (<c>rs:fill:240:180</c>).</summary>
    public const string Thumbnail = "thumb";

    /// <summary>Card-sized image (<c>rs:fill:600:450</c>).</summary>
    public const string Card = "card";

    /// <summary>Detail page image (<c>rs:fit:1200:1200</c>, never enlarged).</summary>
    public const string Detail = "detail";

    /// <summary>Fullscreen viewer image (<c>rs:fit:1920:1920</c>, never enlarged).</summary>
    public const string Fullscreen = "full";
}
