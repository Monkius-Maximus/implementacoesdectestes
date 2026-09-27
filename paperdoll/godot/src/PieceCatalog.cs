using System.Collections.Generic;
using System.Linq;
using Godot;

namespace PaperDoll;

/// <summary>
/// Acesso às peças exportadas em res://assets/&lt;slot&gt;/&lt;piece_id&gt;/.
/// Em builds exportados, inclua "*.json" no filtro de recursos não-importados do preset de exportação.
/// </summary>
public sealed class PieceCatalog : IPieceMetaSource
{
    private const string AssetsRoot = "res://assets";
    private readonly Dictionary<(string Slot, string Id), PieceMeta> _metas = new();

    public bool HasPiece(string slot, string pieceId) =>
        Godot.FileAccess.FileExists(MetaPath(slot, pieceId));

    /// <summary>Peças disponíveis num slot, em ordem alfabética. Slot sem pasta = nenhuma peça.</summary>
    public IReadOnlyList<string> Pieces(string slot)
    {
        string dir = $"{AssetsRoot}/{slot}";
        if (!DirAccess.DirExistsAbsolute(dir))
            return [];
        return ResourceLoader.ListDirectory(dir)
            .Where(entry => entry.EndsWith('/'))
            .Select(entry => entry.TrimEnd('/'))
            .Order()
            .ToList();
    }

    public PieceMeta GetMeta(string slot, string pieceId)
    {
        if (!_metas.TryGetValue((slot, pieceId), out PieceMeta? meta))
        {
            meta = PaperDollJson.Load<PieceMeta>(MetaPath(slot, pieceId));
            _metas[(slot, pieceId)] = meta;
        }
        return meta;
    }

    public Texture2D Shade(string slot, string pieceId) => LoadTexture($"{PieceDir(slot, pieceId)}/shade.png");

    public Texture2D Mask(string slot, string pieceId) => LoadTexture($"{PieceDir(slot, pieceId)}/mask.png");

    /// <summary>Camada de contorno com cor fixa. É opcional por peça.</summary>
    public Texture2D? Line(string slot, string pieceId)
    {
        string path = $"{PieceDir(slot, pieceId)}/line.png";
        return ResourceLoader.Exists(path) ? LoadTexture(path) : null;
    }

    private static string PieceDir(string slot, string pieceId) => $"{AssetsRoot}/{slot}/{pieceId}";

    private static string MetaPath(string slot, string pieceId) => $"{PieceDir(slot, pieceId)}/meta.json";

    private static Texture2D LoadTexture(string path) =>
        GD.Load<Texture2D>(path) ?? throw new System.IO.FileNotFoundException($"textura não encontrada: {path}");
}
