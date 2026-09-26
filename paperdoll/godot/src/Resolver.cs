using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaperDoll;

/// <summary>Fonte dos meta.json das peças. Separada para o Resolver não depender do Godot.</summary>
public interface IPieceMetaSource
{
    bool HasPiece(string slot, string pieceId);
    PieceMeta GetMeta(string slot, string pieceId);
}

/// <summary>Uma camada pronta para o bake: peça visível, com cores e decalques já conferidos.</summary>
public sealed record PlannedLayer(
    string Slot,
    string PieceId,
    IReadOnlyDictionary<string, string> Colors,
    IReadOnlyList<PlannedDecal> Decals);

public sealed record PlannedDecal(DecalPlacement Placement, DecalZone Zone);

/// <summary>Camadas visíveis ordenadas por z_order (do fundo para a frente).</summary>
public sealed record BakePlan(IReadOnlyList<PlannedLayer> Layers);

public sealed class DescriptorException(IReadOnlyList<string> errors)
    : Exception("descriptor inválido:\n - " + string.Join("\n - ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

/// <summary>
/// Transforma um CharacterDescriptor em BakePlan. Não renderiza nada: é C# puro e testável.
/// Reúne todos os problemas do descriptor e falha de uma vez com a lista completa.
/// </summary>
public static class Resolver
{
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$");
    private static readonly int[] Rotations = [0, 90, 180, 270];

    public static BakePlan Resolve(CharacterDescriptor descriptor, Contract contract, IPieceMetaSource metas)
    {
        var errors = new List<string>();
        var equipped = new List<(string Slot, EquippedPiece Piece, PieceMeta Meta)>();

        foreach (var (slot, piece) in descriptor.Pieces)
        {
            if (!contract.Slots.ContainsKey(slot))
            {
                errors.Add($"slot '{slot}' não existe no contrato");
                continue;
            }
            if (!metas.HasPiece(slot, piece.Id))
            {
                errors.Add($"{slot}: peça '{piece.Id}' não existe");
                continue;
            }
            PieceMeta meta = metas.GetMeta(slot, piece.Id);
            CheckColors(slot, piece, meta, errors);
            CheckDecals(slot, piece, meta, errors);
            equipped.Add((slot, piece, meta));
        }

        var providedTags = equipped.SelectMany(e => e.Meta.Tags).ToHashSet();
        foreach (var (slot, piece, meta) in equipped)
        {
            foreach (string tag in meta.RequiresTags.Where(tag => !providedTags.Contains(tag)))
                errors.Add($"{slot}/{piece.Id}: exige a tag '{tag}', que nenhuma peça equipada fornece");
        }

        if (errors.Count > 0)
            throw new DescriptorException(errors);

        var hiddenSlots = equipped.SelectMany(e => e.Meta.HidesSlots).ToHashSet();
        var layers = equipped
            .Where(e => !hiddenSlots.Contains(e.Slot))
            .OrderBy(e => contract.Slots[e.Slot].ZOrder)
            .Select(e => new PlannedLayer(
                e.Slot,
                e.Piece.Id,
                e.Piece.Colors,
                e.Piece.Decals
                    .Select(d => new PlannedDecal(d, e.Meta.DecalZones.Single(z => z.Name == d.Zone)))
                    .ToList()))
            .ToList();

        return new BakePlan(layers);
    }

    private static void CheckColors(string slot, EquippedPiece piece, PieceMeta meta, List<string> errors)
    {
        var declared = meta.Regions.Keys.ToHashSet();
        var given = piece.Colors.Keys.ToHashSet();

        foreach (string missing in declared.Except(given))
            errors.Add($"{slot}/{piece.Id}: falta a cor da região '{missing}'");
        foreach (string extra in given.Except(declared))
            errors.Add($"{slot}/{piece.Id}: cor para região '{extra}', que a peça não declara");
        foreach (var (region, color) in piece.Colors.Where(c => !HexColor.IsMatch(c.Value)))
            errors.Add($"{slot}/{piece.Id}: cor '{color}' da região '{region}' não é #RRGGBB");
    }

    private static void CheckDecals(string slot, EquippedPiece piece, PieceMeta meta, List<string> errors)
    {
        var zones = meta.DecalZones.Select(z => z.Name).ToHashSet();
        foreach (DecalPlacement decal in piece.Decals)
        {
            string label = $"{slot}/{piece.Id}: decalque '{decal.Image}'";
            if (!zones.Contains(decal.Zone))
                errors.Add($"{label} usa a zona '{decal.Zone}', que a peça não tem");
            if (decal.Position.Length != 2 || decal.Position[0] < 0 || decal.Position[1] < 0)
                errors.Add($"{label}: position precisa ser [x, y] não negativo");
            if (decal.Scale < 1)
                errors.Add($"{label}: scale precisa ser inteiro >= 1");
            if (!Rotations.Contains(decal.Rotation))
                errors.Add($"{label}: rotation precisa ser 0, 90, 180 ou 270");
        }
    }
}
