using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PaperDoll;

/// <summary>Decalques disponíveis em res://decals/&lt;nome&gt;.png.</summary>
public static class DecalLibrary
{
    private const string Root = "res://decals";

    public static IReadOnlyList<string> Names() =>
        ResourceLoader.ListDirectory(Root)
            .Where(file => file.EndsWith(".png"))
            .Select(file => file[..^".png".Length])
            .Order()
            .ToList();

    /// <summary>Imagem do decalque em RGBA8, sem transformação.</summary>
    public static Image LoadImage(string name)
    {
        string path = TexturePath(name);
        Image image = LoadTexture(path).GetImage();
        if (image.IsCompressed())
            throw new System.InvalidOperationException($"{path}: importe como Lossless (compressão VRAM não é suportada)");
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>Tamanho que o decalque ocupa depois de rotação e escala.</summary>
    public static Vector2I PlacedSize(DecalPlacement placement)
    {
        Vector2I size = (Vector2I)LoadTexture(TexturePath(placement.Image)).GetSize();
        if (placement.Rotation % 180 != 0)
            size = new Vector2I(size.Y, size.X);
        return size * placement.Scale;
    }

    private static string TexturePath(string name) => $"{Root}/{name}.png";

    private static Texture2D LoadTexture(string path) =>
        GD.Load<Texture2D>(path) ?? throw new System.IO.FileNotFoundException($"decalque não encontrado: {path}");
}
