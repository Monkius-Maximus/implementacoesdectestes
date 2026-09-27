using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using PaperDoll;

/// <summary>
/// Editor de personagem do jogador. Toda a interface só altera o CharacterDescriptor (via DescriptorEdits);
/// o preview é sempre a textura assada pelo BakeCompositor, exatamente como aparece no jogo.
///
/// Mudanças rápidas (arrastar a roda de cor) não enfileiram bakes: o editor marca o descriptor como
/// "sujo" e, quando o bake anterior termina, assa apenas o estado mais recente.
/// </summary>
public partial class CharacterEditor : Control
{
    private const string ContractPath = "res://data/contract.json";
    private const string StartDescriptorPath = "res://demo/sample_character.json";
    private const string SaveDir = "user://characters";
    private const string NoPiece = "— nenhuma —";
    private const double FrameSeconds = 0.18;
    private static readonly Regex SaveName = new("^[a-z0-9_-]+$");
    private static readonly int[] Rotations = [0, 90, 180, 270];
    private static readonly Color ErrorColor = new(1f, 0.45f, 0.45f);
    private static readonly Color InfoColor = new(0.6f, 0.85f, 0.6f);

    private readonly Dictionary<string, VBoxContainer> _slotSections = new();
    private Contract _contract = null!;
    private PieceCatalog _catalog = null!;
    private BakeCompositor _compositor = null!;
    private CharacterDescriptor _descriptor = null!;

    private Control _previewArea = null!;
    private Sprite2D _preview = null!;
    private Label _status = null!;
    private LineEdit _saveName = null!;
    private OptionButton _savedList = null!;

    private bool _dirty;
    private bool _baking;
    private bool _statusIsError;
    private double _elapsed;
    private int _frame;

    public CharacterDescriptor Descriptor => _descriptor;
    public bool HasPreview => _preview.Texture != null;
    public string Status => _status.Text;

    public override void _Ready()
    {
        _contract = PaperDollJson.Load<Contract>(ContractPath);
        _catalog = new PieceCatalog();
        _compositor = BakeCompositor.Create(_contract, _catalog);
        AddChild(_compositor);

        BuildLayout();
        Open(PaperDollJson.Load<CharacterDescriptor>(StartDescriptorPath));
        RefreshSavedList(null);
    }

    /// <summary>Abre um descriptor. Lança DescriptorException se for inválido, e nesse caso o editor não muda.</summary>
    public void Open(CharacterDescriptor descriptor)
    {
        Resolver.Resolve(descriptor, _contract, _catalog);
        _descriptor = descriptor;
        foreach (string slot in _slotSections.Keys)
            RebuildSlot(slot);
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        AdvanceFrame(delta);
        if (_dirty && !_baking)
            BakePreview();
    }

    // ------------------------------------------------------------------ bake e preview

    private void SetDescriptor(CharacterDescriptor descriptor)
    {
        _descriptor = descriptor;
        _dirty = true;
    }

    private async void BakePreview()
    {
        _dirty = false;
        _baking = true;
        try
        {
            BakePlan plan = Resolver.Resolve(_descriptor, _contract, _catalog);
            _preview.Texture = await _compositor.Bake(plan);
            if (_statusIsError)
                ShowStatus("", isError: false);
        }
        catch (DescriptorException e)
        {
            // O preview continua mostrando o último estado válido.
            ShowStatus(e.Message, isError: true);
        }
        finally
        {
            _baking = false;
        }
    }

    private void AdvanceFrame(double delta)
    {
        _elapsed += delta;
        if (_elapsed < FrameSeconds)
            return;
        _elapsed -= FrameSeconds;
        _frame = (_frame + 1) % _contract.Canvas.Frames;
        _preview.Frame = _frame;
    }

    private void CenterPreview()
    {
        Vector2 area = _previewArea.Size;
        float scale = Mathf.Max(1f, Mathf.Floor(Mathf.Min(
            area.X / _contract.Canvas.FrameWidth,
            area.Y / _contract.Canvas.FrameHeight)));
        _preview.Scale = Vector2.One * scale;
        _preview.Position = area / 2;
    }

    private void ShowStatus(string text, bool isError)
    {
        _status.Text = text;
        _statusIsError = isError;
        _status.AddThemeColorOverride("font_color", isError ? ErrorColor : InfoColor);
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        AddChild(margin);

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 16);
        margin.AddChild(columns);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(560, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        columns.AddChild(scroll);

        var slots = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        slots.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(slots);

        // Do slot mais à frente para o mais atrás: chapéu e cabelo no topo da lista, corpo embaixo.
        foreach (string slot in _contract.Slots.OrderByDescending(s => s.Value.ZOrder).Select(s => s.Key))
        {
            var section = new VBoxContainer();
            slots.AddChild(section);
            slots.AddChild(new HSeparator());
            _slotSections[slot] = section;
        }

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(right);
        right.AddChild(BuildToolbar());

        _previewArea = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, ClipContents = true };
        _previewArea.Resized += CenterPreview;
        right.AddChild(_previewArea);

        _preview = new Sprite2D
        {
            Material = BakeCompositor.BakedMaterial,
            Hframes = _contract.Canvas.Frames,
        };
        _previewArea.AddChild(_preview);

        _status = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 96),
        };
        right.AddChild(_status);
    }

    private Control BuildToolbar()
    {
        var bar = new HBoxContainer();
        _saveName = new LineEdit { PlaceholderText = "nome_do_personagem", CustomMinimumSize = new Vector2(200, 0) };
        bar.AddChild(_saveName);
        bar.AddChild(MakeButton("Salvar", SaveCurrent));
        _savedList = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
        bar.AddChild(_savedList);
        bar.AddChild(MakeButton("Carregar", LoadSelected));
        bar.AddChild(MakeButton("Cores aleatórias", RandomizeColors));
        return bar;
    }

    // ------------------------------------------------------------------ seções por slot

    private void RebuildSlotDeferred(string slot) => Callable.From(() => RebuildSlot(slot)).CallDeferred();

    private void RebuildSlot(string slot)
    {
        VBoxContainer section = _slotSections[slot];
        foreach (Node child in section.GetChildren())
        {
            section.RemoveChild(child);
            child.QueueFree();
        }

        var header = new HBoxContainer();
        header.AddChild(new Label
        {
            Text = slot,
            ThemeTypeVariation = "HeaderSmall",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        header.AddChild(BuildPiecePicker(slot));
        section.AddChild(header);

        if (!_descriptor.Pieces.TryGetValue(slot, out EquippedPiece? piece))
            return;

        PieceMeta meta = _catalog.GetMeta(slot, piece.Id);
        ContractCategory category = _contract.Categories[_contract.Slots[slot].Category];
        foreach (string region in meta.Regions.Keys)
            section.AddChild(BuildColorRow(slot, region, category.Regions[region], piece.Colors[region]));

        if (meta.DecalZones.Count == 0)
            return;

        for (int index = 0; index < piece.Decals.Count; index++)
            section.AddChild(BuildDecalRow(slot, index, piece.Decals[index], meta));

        IReadOnlyList<string> images = DecalLibrary.Names();
        Button add = MakeButton("+ decalque", () =>
        {
            var decal = new DecalPlacement(images[0], meta.DecalZones[0].Name, [0, 0], 1, 0, false);
            SetDescriptor(DescriptorEdits.AddDecal(_descriptor, slot, decal));
            RebuildSlotDeferred(slot);
        });
        add.Disabled = images.Count == 0;
        add.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        section.AddChild(add);
    }

    private OptionButton BuildPiecePicker(string slot)
    {
        var picker = new OptionButton { CustomMinimumSize = new Vector2(180, 0) };
        picker.AddItem(NoPiece);
        foreach (string id in _catalog.Pieces(slot))
            picker.AddItem(id);

        string current = _descriptor.Pieces.TryGetValue(slot, out EquippedPiece? piece) ? piece.Id : NoPiece;
        picker.Select(IndexOfText(picker, current));

        picker.ItemSelected += index =>
        {
            string id = picker.GetItemText((int)index);
            SetDescriptor(id == NoPiece
                ? DescriptorEdits.Unequip(_descriptor, slot)
                : DescriptorEdits.Equip(_descriptor, slot, _catalog.GetMeta(slot, id)));
            RebuildSlotDeferred(slot);
        };
        return picker;
    }

    private Control BuildColorRow(string slot, string region, string label, string hex)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(140, 0) });
        var picker = new ColorPickerButton
        {
            Color = Color.FromHtml(hex),
            EditAlpha = false,
            CustomMinimumSize = new Vector2(120, 28),
        };
        picker.ColorChanged += color =>
            SetDescriptor(DescriptorEdits.SetColor(_descriptor, slot, region, "#" + color.ToHtml(false)));
        row.AddChild(picker);
        return row;
    }

    private Control BuildDecalRow(string slot, int index, DecalPlacement decal, PieceMeta meta)
    {
        IReadOnlyList<string> images = DecalLibrary.Names();
        List<string> zones = meta.DecalZones.Select(z => z.Name).ToList();

        OptionButton image = MakeOptions(images, decal.Image);
        OptionButton zone = MakeOptions(zones, decal.Zone);
        SpinBox x = MakeSpin("x ", 0, 999, decal.Position[0]);
        SpinBox y = MakeSpin("y ", 0, 999, decal.Position[1]);
        SpinBox scale = MakeSpin("×", 1, 8, decal.Scale);
        OptionButton rotation = MakeOptions(Rotations.Select(r => $"{r}°").ToList(), $"{decal.Rotation}°");
        var flip = new CheckBox { Text = "espelhar", ButtonPressed = decal.FlipX };

        DecalPlacement Read() => new(
            images[image.Selected],
            zones[zone.Selected],
            [(int)x.Value, (int)y.Value],
            (int)scale.Value,
            Rotations[rotation.Selected],
            flip.ButtonPressed);

        // A posição máxima depende da zona e do tamanho do decalque já rotacionado e escalado.
        void Limit(DecalPlacement placement)
        {
            int[] rect = meta.DecalZones.Single(z => z.Name == placement.Zone).Rect;
            Vector2I size = DecalLibrary.PlacedSize(placement);
            x.MaxValue = Mathf.Max(0, rect[2] - size.X);
            y.MaxValue = Mathf.Max(0, rect[3] - size.Y);
        }

        bool applying = false;
        void Apply()
        {
            if (applying)
                return;
            applying = true;
            Limit(Read());
            SetDescriptor(DescriptorEdits.SetDecal(_descriptor, slot, index, Read()));
            applying = false;
        }

        Limit(decal);
        image.ItemSelected += _ => Apply();
        zone.ItemSelected += _ => Apply();
        x.ValueChanged += _ => Apply();
        y.ValueChanged += _ => Apply();
        scale.ValueChanged += _ => Apply();
        rotation.ItemSelected += _ => Apply();
        flip.Toggled += _ => Apply();

        Button remove = MakeButton("✕", () =>
        {
            SetDescriptor(DescriptorEdits.RemoveDecal(_descriptor, slot, index));
            RebuildSlotDeferred(slot);
        });

        var row = new HFlowContainer();
        foreach (Control control in new Control[] { image, zone, x, y, scale, rotation, flip, remove })
            row.AddChild(control);
        return row;
    }

    // ------------------------------------------------------------------ salvar, carregar, aleatório

    private void SaveCurrent()
    {
        string name = _saveName.Text.Trim();
        if (!SaveName.IsMatch(name))
        {
            ShowStatus("nome inválido: use só a-z, 0-9, _ e -", isError: true);
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(SaveDir);
        string path = $"{SaveDir}/{name}.json";
        using (Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write)
            ?? throw new System.IO.IOException($"não foi possível gravar {path}: {Godot.FileAccess.GetOpenError()}"))
        {
            file.StoreString(PaperDollJson.Serialize(_descriptor));
        }
        RefreshSavedList(name);
        ShowStatus($"salvo em {ProjectSettings.GlobalizePath(path)}", isError: false);
    }

    private void LoadSelected()
    {
        if (_savedList.ItemCount == 0)
        {
            ShowStatus("nenhum personagem salvo ainda", isError: true);
            return;
        }

        string name = _savedList.GetItemText(_savedList.Selected);
        try
        {
            Open(PaperDollJson.Load<CharacterDescriptor>($"{SaveDir}/{name}.json"));
            _saveName.Text = name;
            ShowStatus($"carregado: {name}", isError: false);
        }
        catch (DescriptorException e)
        {
            ShowStatus(e.Message, isError: true);
        }
        catch (System.Text.Json.JsonException e)
        {
            ShowStatus($"{name}.json inválido: {e.Message}", isError: true);
        }
    }

    private void RandomizeColors()
    {
        CharacterDescriptor descriptor = _descriptor;
        foreach (var (slot, piece) in _descriptor.Pieces)
        {
            foreach (string region in piece.Colors.Keys)
                descriptor = DescriptorEdits.SetColor(descriptor, slot, region, RandomColor());
        }
        SetDescriptor(descriptor);
        foreach (string slot in _slotSections.Keys)
            RebuildSlot(slot);
    }

    private void RefreshSavedList(string? select)
    {
        _savedList.Clear();
        if (!DirAccess.DirExistsAbsolute(SaveDir))
            return;
        foreach (string file in DirAccess.GetFilesAt(SaveDir).Where(f => f.EndsWith(".json")).Order())
            _savedList.AddItem(file[..^".json".Length]);
        if (select != null)
            _savedList.Select(IndexOfText(_savedList, select));
    }

    // ------------------------------------------------------------------ utilitários de UI

    private static string RandomColor() =>
        "#" + Color.FromHsv(GD.Randf(), (float)GD.RandRange(0.2, 0.9), (float)GD.RandRange(0.3, 1.0)).ToHtml(false);

    private static int IndexOfText(OptionButton options, string text)
    {
        for (int i = 0; i < options.ItemCount; i++)
        {
            if (options.GetItemText(i) == text)
                return i;
        }
        throw new KeyNotFoundException($"opção '{text}' não existe");
    }

    private static Button MakeButton(string text, System.Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += onPressed;
        return button;
    }

    private static OptionButton MakeOptions(IReadOnlyList<string> items, string selected)
    {
        var options = new OptionButton();
        foreach (string item in items)
            options.AddItem(item);
        options.Select(IndexOfText(options, selected));
        return options;
    }

    private static SpinBox MakeSpin(string prefix, int min, int max, int value) => new()
    {
        Prefix = prefix,
        MinValue = min,
        MaxValue = max,
        Step = 1,
        Rounded = true,
        Value = value,
    };
}
