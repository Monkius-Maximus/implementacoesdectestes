using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaperDoll;

/// <summary>Espelho do meta.json de uma peça (já validado por tools/validate_assets.py).</summary>
public sealed record PieceMeta(
    [property: JsonRequired] string Id,
    [property: JsonRequired] Dictionary<string, RegionDefault> Regions,
    [property: JsonRequired] List<DecalZone> DecalZones,
    [property: JsonRequired] List<string> Tags,
    [property: JsonRequired] List<string> HidesSlots,
    [property: JsonRequired] List<string> RequiresTags);

/// <summary>Cor usada quando o editor equipa a peça. Nunca é consultada durante o bake.</summary>
public sealed record RegionDefault(
    [property: JsonRequired] string Default);

/// <summary>
/// Área onde decalques são permitidos. Rect = [x, y, largura, altura] no frame 0.
/// FrameOffsets tem uma entrada por frame: [dx, dy], ou null quando a zona não aparece naquele frame.
/// </summary>
public sealed record DecalZone(
    [property: JsonRequired] string Name,
    [property: JsonRequired] int[] Rect,
    [property: JsonRequired] int[]?[] FrameOffsets);
