using Godot;

namespace PaperDoll;

/// <summary>
/// Monta, na CPU, a camada de decalques de uma peça no mesmo layout de sprite sheet do shade e da máscara.
/// O shader da peça aplica essa camada antes do sombreamento, então o decalque recebe luz e volume.
/// </summary>
public sealed class DecalRasterizer
{
    private const string DecalsRoot = "res://decals";
    private readonly CanvasSpec _canvas;
    private readonly Texture2D _empty;

    public DecalRasterizer(CanvasSpec canvas)
    {
        _canvas = canvas;
        // Peças sem decalque recebem esta textura transparente: o shader é sempre o mesmo.
        _empty = ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));
    }

    public Texture2D Rasterize(PlannedLayer layer)
    {
        if (layer.Decals.Count == 0)
            return _empty;

        Image sheet = Image.CreateEmpty(_canvas.SheetWidth, _canvas.FrameHeight, false, Image.Format.Rgba8);
        foreach (PlannedDecal decal in layer.Decals)
            Stamp(sheet, decal, layer);
        return ImageTexture.CreateFromImage(sheet);
    }

    private void Stamp(Image sheet, PlannedDecal decal, PlannedLayer layer)
    {
        DecalPlacement placement = decal.Placement;
        int[] zone = decal.Zone.Rect;
        Image image = LoadDecal(placement.Image);

        if (placement.FlipX)
            image.FlipX();
        for (int turns = placement.Rotation / 90; turns > 0; turns--)
            image.Rotate90(ClockDirection.Clockwise);
        if (placement.Scale > 1)
            image.Resize(image.GetWidth() * placement.Scale, image.GetHeight() * placement.Scale, Image.Interpolation.Nearest);

        Vector2I size = image.GetSize();
        int x = placement.Position[0];
        int y = placement.Position[1];
        if (x + size.X > zone[2] || y + size.Y > zone[3])
        {
            throw new DescriptorException([
                $"{layer.Slot}/{layer.PieceId}: decalque '{placement.Image}' ({size.X}x{size.Y} em [{x}, {y}]) " +
                $"não cabe na zona '{decal.Zone.Name}' ({zone[2]}x{zone[3]})"
            ]);
        }

        var source = new Rect2I(Vector2I.Zero, size);
        for (int frame = 0; frame < _canvas.Frames; frame++)
        {
            int[]? offset = decal.Zone.FrameOffsets[frame];
            if (offset == null)
                continue;
            var target = new Vector2I(
                frame * _canvas.FrameWidth + zone[0] + offset[0] + x,
                zone[1] + offset[1] + y);
            sheet.BlendRect(image, source, target);
        }
    }

    private static Image LoadDecal(string name)
    {
        string path = $"{DecalsRoot}/{name}.png";
        Texture2D texture = GD.Load<Texture2D>(path)
            ?? throw new System.IO.FileNotFoundException($"decalque não encontrado: {path}");
        Image image = texture.GetImage();
        if (image.IsCompressed())
            throw new System.InvalidOperationException($"{path}: importe como Lossless (compressão VRAM não é suportada)");
        image.Convert(Image.Format.Rgba8);
        return image;
    }
}
