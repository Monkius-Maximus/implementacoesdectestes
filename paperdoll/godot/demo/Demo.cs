using System.Collections.Generic;
using System.Linq;
using Godot;
using PaperDoll;

/// <summary>
/// Assa o personagem de exemplo e 4 variações de cor (cabelo, camisa e calça aleatórios)
/// e anima todos a partir das texturas assadas. Espaço/Enter gera novas variações.
/// </summary>
public partial class Demo : Node2D
{
    private const string ContractPath = "res://data/contract.json";
    private const string DescriptorPath = "res://demo/sample_character.json";
    private const int Variations = 4;
    private const int DisplayScale = 4;
    private const int Margin = 24;
    private const double FrameSeconds = 0.18;
    private static readonly string[] RecoloredSlots = ["hair_front", "torso", "legs"];

    private readonly List<Sprite2D> _characters = new();
    private Contract _contract = null!;
    private PieceCatalog _catalog = null!;
    private BakeCompositor _compositor = null!;
    private CharacterDescriptor _original = null!;
    private double _elapsed;
    private int _frame;
    private bool _baking;

    public override async void _Ready()
    {
        _contract = PaperDollJson.Load<Contract>(ContractPath);
        _catalog = new PieceCatalog();
        _compositor = BakeCompositor.Create(_contract, _catalog);
        AddChild(_compositor);
        _original = PaperDollJson.Load<CharacterDescriptor>(DescriptorPath);

        await BakeAll();
    }

    public override async void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept") && !_baking)
            await BakeAll();
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < FrameSeconds)
            return;
        _elapsed -= FrameSeconds;
        _frame = (_frame + 1) % _contract.Canvas.Frames;
        foreach (Sprite2D character in _characters)
            character.Frame = _frame;
    }

    private async System.Threading.Tasks.Task BakeAll()
    {
        _baking = true;
        var descriptors = new List<CharacterDescriptor> { _original };
        for (int i = 0; i < Variations; i++)
            descriptors.Add(Recolor(_original));

        foreach (Sprite2D old in _characters)
            old.QueueFree();
        _characters.Clear();

        int step = _contract.Canvas.FrameWidth * DisplayScale + Margin;
        for (int i = 0; i < descriptors.Count; i++)
        {
            BakePlan plan = Resolver.Resolve(descriptors[i], _contract, _catalog);
            Texture2D texture = await _compositor.Bake(plan);
            var sprite = new Sprite2D
            {
                Texture = texture,
                Material = BakeCompositor.BakedMaterial,
                Hframes = _contract.Canvas.Frames,
                Frame = _frame,
                Centered = false,
                Scale = Vector2.One * DisplayScale,
                Position = new Vector2(Margin + i * step, Margin),
            };
            AddChild(sprite);
            _characters.Add(sprite);
        }
        _baking = false;
    }

    private static CharacterDescriptor Recolor(CharacterDescriptor descriptor) => descriptor with
    {
        Pieces = descriptor.Pieces.ToDictionary(
            slot => slot.Key,
            slot => RecoloredSlots.Contains(slot.Key)
                ? slot.Value with { Colors = slot.Value.Colors.ToDictionary(c => c.Key, _ => RandomColor()) }
                : slot.Value),
    };

    private static string RandomColor() =>
        "#" + Color.FromHsv(GD.Randf(), (float)GD.RandRange(0.3, 0.9), (float)GD.RandRange(0.35, 1.0)).ToHtml(false);
}
