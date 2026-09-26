using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace PaperDoll;

/// <summary>
/// Achata um BakePlan numa única textura (sprite sheet do personagem inteiro) usando um SubViewport reutilizado.
///
/// A textura devolvida tem cores PRÉ-MULTIPLICADAS pelo alpha (comportamento do SubViewport com fundo
/// transparente, godotengine/godot#99715). Desenhe-a sempre com <see cref="BakedMaterial"/>.
/// </summary>
public partial class BakeCompositor : Node
{
    private const string PieceShaderPath = "res://shaders/paperdoll_piece.gdshader";

    private static CanvasItemMaterial? _bakedMaterial;

    /// <summary>Material obrigatório para qualquer CanvasItem que desenhe uma textura assada.</summary>
    public static CanvasItemMaterial BakedMaterial =>
        _bakedMaterial ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private PieceCatalog _catalog = null!;
    private DecalRasterizer _rasterizer = null!;
    private Shader _pieceShader = null!;
    private SubViewport _viewport = null!;

    /// <summary>Cria o compositor. Adicione-o à árvore antes de chamar <see cref="Bake"/>.</summary>
    public static BakeCompositor Create(Contract contract, PieceCatalog catalog)
    {
        CanvasSpec canvas = contract.Canvas;
        var compositor = new BakeCompositor
        {
            Name = nameof(BakeCompositor),
            _catalog = catalog,
            _rasterizer = new DecalRasterizer(canvas),
            _pieceShader = GD.Load<Shader>(PieceShaderPath)
                ?? throw new System.IO.FileNotFoundException($"shader não encontrado: {PieceShaderPath}"),
            _viewport = new SubViewport
            {
                Size = new Vector2I(canvas.SheetWidth, canvas.FrameHeight),
                TransparentBg = true,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            },
        };
        compositor.AddChild(compositor._viewport);
        return compositor;
    }

    /// <summary>
    /// Renderiza o plano e devolve a textura assada. Bakes simultâneos são enfileirados e rodam um por frame.
    /// </summary>
    public async Task<Texture2D> Bake(BakePlan plan)
    {
        if (!IsInsideTree())
            throw new System.InvalidOperationException("BakeCompositor precisa estar na árvore de cena para renderizar");

        await _gate.WaitAsync();
        try
        {
            ClearViewport();
            foreach (PlannedLayer layer in plan.Layers)
            {
                _viewport.AddChild(CreatePieceSprite(layer));
                Texture2D? line = _catalog.Line(layer.Slot, layer.PieceId);
                if (line != null)
                    _viewport.AddChild(CreateSprite(line));
            }

            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            return ImageTexture.CreateFromImage(_viewport.GetTexture().GetImage());
        }
        finally
        {
            _gate.Release();
        }
    }

    private Sprite2D CreatePieceSprite(PlannedLayer layer)
    {
        var material = new ShaderMaterial { Shader = _pieceShader };
        material.SetShaderParameter("mask_tex", _catalog.Mask(layer.Slot, layer.PieceId));
        material.SetShaderParameter("decal_tex", _rasterizer.Rasterize(layer));
        foreach (var (region, hex) in layer.Colors)
            material.SetShaderParameter(ColorParameter(region), Color.FromHtml(hex));

        Sprite2D sprite = CreateSprite(_catalog.Shade(layer.Slot, layer.PieceId));
        sprite.Material = material;
        return sprite;
    }

    private static Sprite2D CreateSprite(Texture2D texture) => new()
    {
        Texture = texture,
        Centered = false,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
    };

    private static string ColorParameter(string region) => region switch
    {
        "base" => "base_color",
        "r" => "color_r",
        "g" => "color_g",
        "b" => "color_b",
        _ => throw new System.ArgumentException($"região desconhecida '{region}'"),
    };

    private void ClearViewport()
    {
        foreach (Node child in _viewport.GetChildren())
        {
            _viewport.RemoveChild(child);
            child.QueueFree();
        }
    }
}
