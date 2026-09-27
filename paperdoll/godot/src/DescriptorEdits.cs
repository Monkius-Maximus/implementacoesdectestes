using System.Collections.Generic;
using System.Linq;

namespace PaperDoll;

/// <summary>
/// Edições sobre um CharacterDescriptor. Cada operação devolve um descriptor novo; o original não muda.
/// C# puro: é aqui que mora a regra "cores padrão do meta só entram quando a peça é equipada".
/// </summary>
public static class DescriptorEdits
{
    /// <summary>Equipa a peça no slot com as cores padrão do meta e sem decalques.</summary>
    public static CharacterDescriptor Equip(CharacterDescriptor descriptor, string slot, PieceMeta meta) =>
        WithPiece(descriptor, slot, new EquippedPiece(
            meta.Id,
            meta.Regions.ToDictionary(region => region.Key, region => region.Value.Default),
            []));

    public static CharacterDescriptor Unequip(CharacterDescriptor descriptor, string slot) =>
        descriptor with
        {
            Pieces = descriptor.Pieces.Where(p => p.Key != slot).ToDictionary(p => p.Key, p => p.Value),
        };

    public static CharacterDescriptor SetColor(CharacterDescriptor descriptor, string slot, string region, string hex)
    {
        EquippedPiece piece = descriptor.Pieces[slot];
        if (!piece.Colors.ContainsKey(region))
            throw new KeyNotFoundException($"{slot}/{piece.Id} não tem a região '{region}'");
        return WithPiece(descriptor, slot, piece with { Colors = new(piece.Colors) { [region] = hex } });
    }

    public static CharacterDescriptor AddDecal(CharacterDescriptor descriptor, string slot, DecalPlacement decal)
    {
        EquippedPiece piece = descriptor.Pieces[slot];
        return WithPiece(descriptor, slot, piece with { Decals = [.. piece.Decals, decal] });
    }

    public static CharacterDescriptor SetDecal(CharacterDescriptor descriptor, string slot, int index, DecalPlacement decal)
    {
        EquippedPiece piece = descriptor.Pieces[slot];
        var decals = new List<DecalPlacement>(piece.Decals) { [index] = decal };
        return WithPiece(descriptor, slot, piece with { Decals = decals });
    }

    public static CharacterDescriptor RemoveDecal(CharacterDescriptor descriptor, string slot, int index)
    {
        EquippedPiece piece = descriptor.Pieces[slot];
        var decals = new List<DecalPlacement>(piece.Decals);
        decals.RemoveAt(index);
        return WithPiece(descriptor, slot, piece with { Decals = decals });
    }

    private static CharacterDescriptor WithPiece(CharacterDescriptor descriptor, string slot, EquippedPiece piece) =>
        descriptor with { Pieces = new(descriptor.Pieces) { [slot] = piece } };
}
