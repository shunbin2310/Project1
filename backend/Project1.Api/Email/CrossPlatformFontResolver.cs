using PdfSharp.Fonts;

namespace Project1.Api.Email;

internal sealed class CrossPlatformFontResolver : IFontResolver
{
    private const string RegularFace = "Project1Sans#Regular";
    private const string BoldFace = "Project1Sans#Bold";

    private readonly Lazy<byte[]> regularFont = new(() => LoadFont(isBold: false));
    private readonly Lazy<byte[]> boldFont = new(() => LoadFont(isBold: true));

    public string DefaultFontName => "Project1 Sans";

    public byte[]? GetFont(string faceName) => faceName switch
    {
        RegularFace => regularFont.Value,
        BoldFace => boldFont.Value,
        _ => null
    };

    public FontResolverInfo? ResolveTypeface(
        string familyName,
        bool isBold,
        bool isItalic) =>
        new(isBold ? BoldFace : RegularFace, mustSimulateBold: false, mustSimulateItalic: isItalic);

    private static byte[] LoadFont(bool isBold)
    {
        var candidates = isBold
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
                "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
                "/System/Library/Fonts/Supplemental/Arial Bold.ttf"
            }
            : new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
                "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                "/usr/share/fonts/dejavu/DejaVuSans.ttf",
                "/System/Library/Fonts/Supplemental/Arial.ttf"
            };

        var fontPath = candidates.FirstOrDefault(File.Exists);
        if (fontPath is null)
        {
            throw new InvalidOperationException(
                "A supported Arial or DejaVu Sans font could not be found for PDF generation.");
        }

        return File.ReadAllBytes(fontPath);
    }
}
