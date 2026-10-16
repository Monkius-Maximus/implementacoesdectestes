using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperDoll;

/// <summary>
/// Gera um CharacterDescriptor aleatório: uma peça por slot (respeitando requires_tags) e cores
/// tiradas das paletas curadas. C# puro; a mesma semente gera sempre o mesmo personagem.
/// Usa as mesmas edições do editor (Equip + SetColor), então o resultado é um descriptor comum.
/// </summary>
public static class NpcGenerator
{
    public static CharacterDescriptor Generate(NpcRules rules, Contract contract, IPieceMetaSource metas, Random random)
    {
        var descriptor = new CharacterDescriptor(new());
        var providedTags = new HashSet<string>();
        var swatchOfGroup = new Dictionary<string, Dictionary<string, string>>();

        foreach (NpcSlotRule rule in rules.Slots)
        {
            if (rule.Chance < 1 && random.NextDouble() >= rule.Chance)
                continue;

            List<PieceMeta> candidates = metas.Pieces(rule.Slot)
                .Select(id => metas.GetMeta(rule.Slot, id))
                .Where(meta => meta.RequiresTags.All(providedTags.Contains))
                .ToList();
            if (candidates.Count == 0)
            {
                if (rule.Chance >= 1)
                    throw new InvalidOperationException($"slot obrigatório '{rule.Slot}' não tem nenhuma peça compatível");
                continue;
            }

            PieceMeta piece = candidates[random.Next(candidates.Count)];
            if (!swatchOfGroup.TryGetValue(rule.ColorGroup, out Dictionary<string, string>? swatch))
            {
                List<Dictionary<string, string>> palette = rules.Palettes[contract.Slots[rule.Slot].Category];
                swatch = palette[random.Next(palette.Count)];
                swatchOfGroup[rule.ColorGroup] = swatch;
            }

            descriptor = DescriptorEdits.Equip(descriptor, rule.Slot, piece);
            foreach (string region in piece.Regions.Keys)
                descriptor = DescriptorEdits.SetColor(descriptor, rule.Slot, region, swatch[region]);
            providedTags.UnionWith(piece.Tags);
        }
        return descriptor;
    }
}
