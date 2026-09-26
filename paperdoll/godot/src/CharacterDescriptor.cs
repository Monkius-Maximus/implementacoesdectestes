using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaperDoll;

/// <summary>
/// A receita de um personagem. É o que se salva, compartilha e gera proceduralmente.
/// Sempre completo: toda região declarada no meta da peça tem sua cor aqui.
/// </summary>
public sealed record CharacterDescriptor(
    [property: JsonRequired] Dictionary<string, EquippedPiece> Pieces);

/// <summary>Peça equipada num slot (a chave do dicionário Pieces é o nome do slot).</summary>
public sealed record EquippedPiece(
    [property: JsonRequired] string Id,
    [property: JsonRequired] Dictionary<string, string> Colors,
    [property: JsonRequired] List<DecalPlacement> Decals);

/// <summary>
/// Decalque aplicado numa zona da peça. Position é relativa ao canto da zona.
/// Rotation em graus: 0, 90, 180 ou 270. Scale é inteiro (pixel art).
/// </summary>
public sealed record DecalPlacement(
    [property: JsonRequired] string Image,
    [property: JsonRequired] string Zone,
    [property: JsonRequired] int[] Position,
    [property: JsonRequired] int Scale,
    [property: JsonRequired] int Rotation,
    [property: JsonRequired] bool FlipX);
