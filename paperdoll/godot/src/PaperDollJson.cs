using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaperDoll;

/// <summary>
/// Leitura de todos os JSON do paper doll (contrato, meta das peças, descriptors).
/// Campos em snake_case, campos desconhecidos são erro e campos marcados [JsonRequired] são obrigatórios.
/// </summary>
public static class PaperDollJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static T Load<T>(string path)
    {
        if (!Godot.FileAccess.FileExists(path))
            throw new System.IO.FileNotFoundException($"arquivo não encontrado: {path}");

        string text = Godot.FileAccess.GetFileAsString(path);
        return JsonSerializer.Deserialize<T>(text, Options)
            ?? throw new System.IO.InvalidDataException($"{path}: JSON vazio");
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
