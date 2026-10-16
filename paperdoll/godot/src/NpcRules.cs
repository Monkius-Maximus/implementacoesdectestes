using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace PaperDoll;

/// <summary>
/// Espelho de data/npc_rules.json: paletas curadas e regras de sorteio do gerador de NPCs.
/// Uma paleta é uma lista de conjuntos de cores coordenados: cada conjunto traz uma cor para
/// TODAS as regiões da categoria (a peça usa só as regiões que declara).
/// </summary>
public sealed record NpcRules(
    [property: JsonRequired] Dictionary<string, List<Dictionary<string, string>>> Palettes,
    [property: JsonRequired] List<NpcSlotRule> Slots)
{
    /// <summary>Carrega o arquivo e confere tudo contra o contrato. Lança InvalidDataException com todos os problemas.</summary>
    public static NpcRules Load(string path, Contract contract)
    {
        NpcRules rules = PaperDollJson.Load<NpcRules>(path);
        List<string> errors = rules.Errors(contract);
        if (errors.Count > 0)
            throw new InvalidDataException($"{path} inválido:\n - " + string.Join("\n - ", errors));
        return rules;
    }

    /// <summary>Problemas das regras contra o contrato. Lista vazia = válidas.</summary>
    public List<string> Errors(Contract contract)
    {
        var errors = new List<string>();

        foreach (var (category, swatches) in Palettes)
        {
            if (!contract.Categories.TryGetValue(category, out ContractCategory? spec))
            {
                errors.Add($"paletas: categoria '{category}' não existe no contrato");
                continue;
            }
            if (swatches.Count == 0)
                errors.Add($"paletas.{category}: nenhum conjunto de cores");
            for (int index = 0; index < swatches.Count; index++)
            {
                Dictionary<string, string> swatch = swatches[index];
                if (!swatch.Keys.ToHashSet().SetEquals(spec.Regions.Keys))
                    errors.Add($"paletas.{category}[{index}]: as regiões devem ser exatamente {string.Join(",", spec.Regions.Keys)}");
                foreach (var (region, hex) in swatch.Where(c => !Resolver.HexColor.IsMatch(c.Value)))
                    errors.Add($"paletas.{category}[{index}].{region}: cor inválida '{hex}'");
            }
        }

        var categoryOfGroup = new Dictionary<string, string>();
        foreach (NpcSlotRule rule in Slots)
        {
            if (!contract.Slots.TryGetValue(rule.Slot, out ContractSlot? slot))
            {
                errors.Add($"slots: '{rule.Slot}' não existe no contrato");
                continue;
            }
            if (Slots.Count(r => r.Slot == rule.Slot) > 1)
                errors.Add($"slots: '{rule.Slot}' aparece mais de uma vez");
            if (rule.Chance is <= 0 or > 1)
                errors.Add($"slots.{rule.Slot}: chance deve estar em (0, 1], veio {rule.Chance}");
            if (!Palettes.TryGetValue(slot.Category, out var palette) || palette.Count == 0)
                errors.Add($"slots.{rule.Slot}: não há paleta para a categoria '{slot.Category}'");
            if (!categoryOfGroup.TryAdd(rule.ColorGroup, slot.Category) && categoryOfGroup[rule.ColorGroup] != slot.Category)
                errors.Add($"slots.{rule.Slot}: o grupo de cor '{rule.ColorGroup}' mistura categorias");
        }
        return errors.Distinct().ToList();
    }
}

/// <summary>
/// Regra de um slot. Chance 1 = sempre equipado; abaixo disso, é a probabilidade de aparecer.
/// Slots com o mesmo ColorGroup usam o mesmo conjunto de cores (ex.: cabelo da frente e de trás).
/// A ordem da lista é a ordem de sorteio: um slot só vê as tags das peças sorteadas antes dele.
/// </summary>
public sealed record NpcSlotRule(
    [property: JsonRequired] string Slot,
    [property: JsonRequired] double Chance,
    [property: JsonRequired] string ColorGroup);
