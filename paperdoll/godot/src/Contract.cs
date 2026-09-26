using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaperDoll;

/// <summary>Espelho de data/contract.json. É a mesma fonte de verdade usada pelo validador Python.</summary>
public sealed record Contract(
    [property: JsonRequired] CanvasSpec Canvas,
    [property: JsonRequired] ShadeLimits Shade,
    [property: JsonRequired] MaskLimits Mask,
    [property: JsonRequired] Dictionary<string, ContractCategory> Categories,
    [property: JsonRequired] Dictionary<string, ContractSlot> Slots,
    [property: JsonRequired] List<string> Tags);

public sealed record CanvasSpec(
    [property: JsonRequired] int FrameWidth,
    [property: JsonRequired] int FrameHeight,
    [property: JsonRequired] int Frames)
{
    /// <summary>Largura do sprite sheet: os frames ficam lado a lado na horizontal.</summary>
    [JsonIgnore] public int SheetWidth => FrameWidth * Frames;
}

public sealed record ShadeLimits(
    [property: JsonRequired] int MaxSaturation,
    [property: JsonRequired] int MinMeanValue);

public sealed record MaskLimits(
    [property: JsonRequired] int SumTolerance);

/// <summary>Significado de cada região ("base", "r", "g", "b") para as peças desta categoria.</summary>
public sealed record ContractCategory(
    [property: JsonRequired] bool MaskGradients,
    [property: JsonRequired] Dictionary<string, string> Regions);

public sealed record ContractSlot(
    [property: JsonRequired] string Category,
    [property: JsonRequired] int ZOrder);
