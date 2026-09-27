using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaperDoll;

/// <summary>
/// Teste de fumaça: resolve e assa o personagem de exemplo, confere as validações do Resolver e do
/// DecalRasterizer, as edições do descriptor, e tira prints da cena demo e do editor.
/// As imagens vão para user://smoke_test/. Sai com código 0 se tudo passou e 1 na primeira falha.
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
            EditsDescriptor();
            await CapturesDemoScene();
            await DrivesEditorScene();

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
        await WaitFrames(60);
        GetViewport().GetTexture().GetImage().SavePng($"{OutputDir}/demo.png");
        RemoveChild(demo);
        demo.QueueFree();
    }

    private void EditsDescriptor()
    {
        Expect(string.Join(",", _catalog.Pieces("torso")) == "shirt_01,shirt_02", "peças do torso");
        Expect(_catalog.Pieces("hat").Count == 1, "peças do chapéu");
        Expect(string.Join(",", DecalLibrary.Names()) == "estrela", "decalques disponíveis");

        var rotated = new DecalPlacement("estrela", "peito", [0, 0], 2, 90, true);
        Expect(DecalLibrary.PlacedSize(rotated) == new Vector2I(14, 14), "tamanho do decalque escalado");

        // Equipar usa as cores padrão do meta e começa sem decalques.
        PieceMeta tank = _catalog.GetMeta("torso", "shirt_01");
        CharacterDescriptor equipped = DescriptorEdits.Equip(_sample, "torso", tank);
        EquippedPiece torso = equipped.Pieces["torso"];
        Expect(torso.Id == "shirt_01" && torso.Decals.Count == 0, "equip deveria trocar a peça e limpar decalques");
        Expect(torso.Colors.Count == 2 && torso.Colors["g"] == tank.Regions["g"].Default, "equip deveria usar as cores padrão");
        Expect(_sample.Pieces["torso"].Id == "shirt_02", "o descriptor original não pode mudar");

        CharacterDescriptor recolored = DescriptorEdits.SetColor(equipped, "torso", "base", "#123456");
        Expect(recolored.Pieces["torso"].Colors["base"] == "#123456", "set color");
        try
        {
            DescriptorEdits.SetColor(equipped, "torso", "r", "#000000");
            throw new Exception("região inexistente deveria falhar");
        }
        catch (KeyNotFoundException)
        {
        }

        var star = new DecalPlacement("estrela", "peito", [1, 1], 1, 0, false);
        CharacterDescriptor withDecal = DescriptorEdits.AddDecal(recolored, "torso", star);
        withDecal = DescriptorEdits.SetDecal(withDecal, "torso", 0, star with { Rotation = 180 });
        Expect(withDecal.Pieces["torso"].Decals.Single().Rotation == 180, "set decal");
        Expect(DescriptorEdits.RemoveDecal(withDecal, "torso", 0).Pieces["torso"].Decals.Count == 0, "remove decal");
        Expect(!DescriptorEdits.Unequip(withDecal, "hair_front").Pieces.ContainsKey("hair_front"), "unequip");
    }

    private async Task DrivesEditorScene()
    {
        var editor = (CharacterEditor)GD.Load<PackedScene>("res://editor/character_editor.tscn").Instantiate();
        AddChild(editor);
        await WaitFrames(30);
        Expect(editor.HasPreview && editor.Status == "", $"o editor deveria abrir com preview e sem erro: {editor.Status}");

        // Um descriptor inválido é recusado e o editor continua no estado anterior.
        CharacterDescriptor before = editor.Descriptor;
        ExpectDescriptorException(() => editor.Open(DescriptorEdits.Unequip(before, "body")));
        Expect(ReferenceEquals(editor.Descriptor, before), "Open inválido não pode alterar o editor");

        // Personagem com todos os slots, uma peça trocada e um decalque transformado.
        CharacterDescriptor full = before;
        full = DescriptorEdits.Equip(full, "hat", _catalog.GetMeta("hat", "beanie_01"));
        full = DescriptorEdits.Equip(full, "hair_back", _catalog.GetMeta("hair_back", "hair_back_01"));
        full = DescriptorEdits.Equip(full, "torso", _catalog.GetMeta("torso", "shirt_01"));
        full = DescriptorEdits.AddDecal(full, "torso", new DecalPlacement("estrela", "peito", [5, 3], 1, 90, false));
        editor.Open(full);
        await WaitFrames(30);
        Expect(editor.Status == "", $"o editor não deveria mostrar erro: {editor.Status}");
        GetViewport().GetTexture().GetImage().SavePng($"{OutputDir}/editor.png");

        // Interações pela própria UI: roda de cor, posição do decalque (com limite da zona) e troca de peça.
        FindAll<ColorPickerButton>(editor).First()
            .EmitSignal(ColorPickerButton.SignalName.ColorChanged, new Color(1, 0, 0));
        Expect(editor.Descriptor.Pieces["hat"].Colors["base"] == "#ff0000", "a roda de cor deveria mudar o descriptor");

        SpinBox decalX = FindAll<SpinBox>(editor).Single(s => s.Prefix == "x ");
        decalX.Value = 999;
        Expect(editor.Descriptor.Pieces["torso"].Decals.Single().Position[0] == 5,
               "a posição do decalque deveria ser limitada pela zona (12 - 7 = 5)");

        OptionButton torsoPicker = FindAll<OptionButton>(editor).Single(o => o.Selected >= 0 && o.GetItemText(o.Selected) == "shirt_01");
        torsoPicker.EmitSignal(OptionButton.SignalName.ItemSelected, IndexOf(torsoPicker, "shirt_02"));
        OptionButton hatPicker = FindAll<OptionButton>(editor).Single(o => o.Selected >= 0 && o.GetItemText(o.Selected) == "beanie_01");
        hatPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
        await WaitFrames(10);
        Expect(editor.Descriptor.Pieces["torso"] is { Id: "shirt_02", Decals.Count: 0 }, "troca de peça pelo seletor");
        Expect(!editor.Descriptor.Pieces.ContainsKey("hat"), "remover peça pelo seletor");
        Expect(editor.Status == "", $"o editor não deveria mostrar erro: {editor.Status}");

        // Salvar e carregar pela barra de ferramentas: o descriptor volta idêntico.
        string saved = PaperDollJson.Serialize(editor.Descriptor);
        FindAll<LineEdit>(editor).Single(l => l.PlaceholderText == "nome_do_personagem").Text = "smoke_test";
        Press(editor, "Salvar");
        Expect(editor.Status.StartsWith("salvo em"), $"salvar: {editor.Status}");
        editor.Open(_sample);
        Press(editor, "Carregar");
        Expect(editor.Status == "carregado: smoke_test", $"carregar: {editor.Status}");
        Expect(PaperDollJson.Serialize(editor.Descriptor) == saved, "o descriptor carregado deveria ser igual ao salvo");
        await WaitFrames(5);
        Expect(editor.Status == "carregado: smoke_test", $"o bake não deveria gerar erro: {editor.Status}");

        RemoveChild(editor);
        editor.QueueFree();
    }

    private static void Press(Node root, string text) =>
        FindAll<Button>(root).Single(b => b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (T nested in FindAll<T>(child))
                yield return nested;
        }
    }

    private static int IndexOf(OptionButton options, string text) =>
        Enumerable.Range(0, options.ItemCount).Single(i => options.GetItemText(i) == text);

    private async Task WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
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
