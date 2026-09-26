using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaperDoll;

/// <summary>
/// Teste de fumaça do compositor: resolve e assa o personagem de exemplo, confere as validações do
/// Resolver e do DecalRasterizer, e tira um print da cena demo. As imagens vão para user://smoke_test/.
/// Sai com código 0 se tudo passou e 1 na primeira falha.
///
/// Editor: abra test/smoke_test.tscn e rode a cena (F6).
/// Linha de comando: godot --path . res://test/smoke_test.tscn
/// </summary>
public partial class SmokeTest : Node
{
    private const string OutputDir = "user://smoke_test";

    private Contract _contract = null!;
    private PieceCatalog _catalog = null!;
    private BakeCompositor _compositor = null!;
    private CharacterDescriptor _sample = null!;

    public override async void _Ready()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(OutputDir);
            _contract = PaperDollJson.Load<Contract>("res://data/contract.json");
            _catalog = new PieceCatalog();
            _compositor = BakeCompositor.Create(_contract, _catalog);
            AddChild(_compositor);
            _sample = PaperDollJson.Load<CharacterDescriptor>("res://demo/sample_character.json");

            await BakesSampleInZOrder();
            await SerializesConcurrentBakes();
            RejectsInvalidDescriptor();
            await RejectsDecalLargerThanZone();
            RejectsIncompleteOrUnknownJson();
            await CapturesDemoScene();

            GD.Print($"OK — imagens em {ProjectSettings.GlobalizePath(OutputDir)}");
            GetTree().Quit(0);
        }
        catch (Exception e)
        {
            GD.PrintErr($"FALHOU: {e}");
            GetTree().Quit(1);
        }
    }

    private async Task BakesSampleInZOrder()
    {
        BakePlan plan = Resolver.Resolve(_sample, _contract, _catalog);
        string order = string.Join(",", plan.Layers.Select(l => l.Slot));
        Expect(order == "body,legs,torso,eyes,hair_front", $"ordem de camadas inesperada: {order}");

        Image image = (await _compositor.Bake(plan)).GetImage();
        Expect(image.GetSize() == new Vector2I(_contract.Canvas.SheetWidth, _contract.Canvas.FrameHeight),
               $"tamanho do bake {image.GetSize()}");
        Expect(image.GetPixel(32, 60).A > 0.99f, "o tronco deveria estar opaco no frame 0");
        Expect(image.GetPixel(1, 1).A == 0f, "o canto do frame deveria estar transparente");
        image.SavePng($"{OutputDir}/baked_sample.png");
    }

    private async Task SerializesConcurrentBakes()
    {
        var withoutHair = _sample with
        {
            Pieces = _sample.Pieces.Where(p => p.Key != "hair_front").ToDictionary(p => p.Key, p => p.Value),
        };
        Task<Texture2D> first = _compositor.Bake(Resolver.Resolve(_sample, _contract, _catalog));
        Task<Texture2D> second = _compositor.Bake(Resolver.Resolve(withoutHair, _contract, _catalog));
        Image a = (await first).GetImage();
        Image b = (await second).GetImage();

        // Pixel do cabelo (frame 0, topo da cabeça): presente no primeiro bake, ausente no segundo.
        Expect(a.GetPixel(32, 10).A > 0.99f, "bake concorrente 1 deveria ter cabelo");
        Expect(b.GetPixel(32, 10).A == 0f, "bake concorrente 2 não deveria ter cabelo");
    }

    private void RejectsInvalidDescriptor()
    {
        var pieces = new Dictionary<string, EquippedPiece>(_sample.Pieces)
        {
            ["cape"] = _sample.Pieces["legs"],
            ["torso"] = _sample.Pieces["torso"] with
            {
                Colors = new() { ["base"] = "blue", ["g"] = "#ffffff" },
                Decals = [new DecalPlacement("estrela", "costas", [0, 0], 0, 45, false)],
            },
        };
        pieces.Remove("body");

        var errors = ExpectDescriptorException(() => Resolver.Resolve(new CharacterDescriptor(pieces), _contract, _catalog));
        Expect(errors.Count == 11, $"esperava 11 erros, veio {errors.Count}:\n{string.Join("\n", errors)}");
    }

    private async Task RejectsDecalLargerThanZone()
    {
        var pieces = new Dictionary<string, EquippedPiece>(_sample.Pieces)
        {
            ["torso"] = _sample.Pieces["torso"] with { Decals = [new DecalPlacement("estrela", "peito", [2, 1], 2, 0, false)] },
        };
        BakePlan plan = Resolver.Resolve(new CharacterDescriptor(pieces), _contract, _catalog);
        try
        {
            await _compositor.Bake(plan);
        }
        catch (DescriptorException e)
        {
            Expect(e.Errors.Single().Contains("não cabe na zona"), e.Message);
            return;
        }
        throw new Exception("decalque maior que a zona deveria falhar");
    }

    private static void RejectsIncompleteOrUnknownJson()
    {
        ExpectJsonException("{\"pieces\":{\"body\":{\"id\":\"body_01\",\"colors\":{}}}}");  // falta "decals"
        ExpectJsonException("{\"pieces\":{},\"extra\":1}");                                  // campo desconhecido
    }

    private async Task CapturesDemoScene()
    {
        Node demo = GD.Load<PackedScene>("res://demo/demo.tscn").Instantiate();
        AddChild(demo);
        for (int i = 0; i < 60; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng($"{OutputDir}/demo.png");
    }

    private static IReadOnlyList<string> ExpectDescriptorException(Action action)
    {
        try
        {
            action();
        }
        catch (DescriptorException e)
        {
            return e.Errors;
        }
        throw new Exception("esperava DescriptorException");
    }

    private static void ExpectJsonException(string json)
    {
        try
        {
            System.Text.Json.JsonSerializer.Deserialize<CharacterDescriptor>(json, PaperDollJson.Options);
        }
        catch (System.Text.Json.JsonException)
        {
            return;
        }
        throw new Exception($"esperava JsonException para: {json}");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
