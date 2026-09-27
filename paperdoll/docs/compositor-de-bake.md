# Compositor de bake

Transforma a receita de um personagem (`CharacterDescriptor`) numa única textura: o sprite sheet do personagem inteiro, com o mesmo layout de frames das peças. O `Sprite2D` do personagem usa `Hframes = frames` e anima normalmente, como se fosse um sprite comum.

```
CharacterDescriptor ─▶ Resolver ─▶ BakePlan ─▶ DecalRasterizer ─▶ BakeCompositor ─▶ Texture2D
     (JSON)           (C# puro)    (dados)       (CPU, Image)       (GPU, SubViewport)   (sprite sheet)
```

Só a última etapa usa a GPU. O editor do jogador, os presets e o gerador de NPCs passam todos pelo mesmo caminho: alteram o descriptor e pedem um bake. Não existe um "modo preview" separado do resultado final.

## Arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/CharacterDescriptor.cs` | A receita: peça por slot, cores por região, decalques. |
| `src/Contract.cs`, `src/PieceMeta.cs` | Espelhos de `contract.json` e `meta.json`. |
| `src/PaperDollJson.cs` | Leitura JSON: snake_case, campos obrigatórios e campos desconhecidos são erro. |
| `src/Resolver.cs` | Descriptor → `BakePlan`. C# puro, sem renderização. |
| `src/PieceCatalog.cs` | Carrega meta e texturas de `res://assets/<slot>/<peça>/`. |
| `src/DecalRasterizer.cs` | Monta a camada de decalques de cada peça na CPU. |
| `src/DecalLibrary.cs` | Decalques disponíveis, imagem e tamanho depois de rotação e escala. |
| `src/BakeCompositor.cs` | Renderiza o plano num `SubViewport` e devolve a textura. |
| `shaders/paperdoll_piece.gdshader` | Máscara → tinta, decalque no albedo, luz do shade. |

## 1. Character Descriptor

```json
{
  "pieces": {
    "torso": {
      "id": "shirt_02",
      "colors": { "base": "#2255aa", "r": "#f2f2f2" },
      "decals": [
        { "image": "estrela", "zone": "peito", "position": [2, 1], "scale": 1, "rotation": 0, "flip_x": false }
      ]
    }
  }
}
```

**O descriptor é sempre completo:** cada região declarada no `meta.json` da peça tem sua cor escrita nele. As cores padrão do meta só entram quando o editor equipa ou troca uma peça, e nunca no momento do bake. Assim um descriptor salvo continua igual mesmo se o padrão de uma peça mudar depois.

Decalques: `image` é o nome do arquivo em `res://decals/` (sem `.png`), `position` é relativa ao canto da zona, `scale` é inteiro e `rotation` aceita 0, 90, 180 ou 270.

## 2. Resolver

`Resolver.Resolve(descriptor, contract, catalog)` devolve o `BakePlan` ou lança `DescriptorException` com **todos** os problemas encontrados:

- o slot existe no contrato e a peça existe nesse slot
- as chaves de `colors` batem exatamente com as `regions` do meta, e cada cor é `#RRGGBB`
- cada decalque aponta para uma zona existente, com `position`, `scale` e `rotation` válidos
- as `requires_tags` são satisfeitas pelas peças equipadas

Depois disso, ele aplica os `hides_slots` e ordena as camadas por `z_order`. Como não renderiza nada, dá para testar com testes unitários comuns. Descriptors inválidos (save corrompido, mod quebrado) morrem aqui, antes de chegar na GPU.

## 3. Decalques entram antes da luz

Um decalque desenhado **por cima** da peça já sombreada parece um adesivo chapado. Uma tatuagem ou estampa precisa ficar **embaixo** da luz: primeiro se monta o albedo (tinta + decalque) e só depois se multiplica pelo shade.

O `DecalRasterizer` monta na CPU, com `Image.BlendRect`, uma camada de decalques por peça, no mesmo layout de sprite sheet. Ele aplica o decalque em cada frame, na posição da zona mais o `frame_offsets` daquele frame, e pula os frames em que o offset é `null`. O shader recebe essa camada como `decal_tex`:

```glsl
vec3 albedo = mix(tint, decal.rgb, decal.a);
COLOR = vec4(shade.rgb * albedo, shade.a);   // shade.a recorta o decalque na silhueta
```

Um passe só, sem blend modes especiais e sem limite de quantidade de decalques. Uma peça sem decalques recebe uma textura transparente compartilhada, então o shader é sempre o mesmo.

**Limitação:** a classe `Image` só gira em passos de 90°. Decalques têm posição, escala inteira, espelhamento e rotação em 90°, o que combina com pixel art. Se um dia a arte for HD e precisar de rotação livre, só a rasterização da camada de decalques muda (passa a ser feita num viewport). O shader continua igual.

Um decalque que não cabe na zona lança `DescriptorException` no momento do bake, porque só aí o tamanho da imagem é conhecido.

## 4. Bake na GPU

Um único `SubViewport` é reutilizado para todos os bakes. Ele tem o tamanho do sheet, `TransparentBg = true`, `Disable3D = true`, e fica parado (`UpdateMode.Disabled`) entre bakes. A cada bake:

1. remove as camadas anteriores do viewport;
2. adiciona um `Sprite2D` por camada, em `(0,0)` e sem centralizar (o contrato de canvas único garante o alinhamento), com o shader da peça. Quando a peça tem `line.png`, ele vem logo em seguida;
3. pede `UpdateMode.Once` e espera `RenderingServer.frame_post_draw`;
4. copia o resultado para uma `ImageTexture` e libera o viewport para o próximo bake.

Os bakes são serializados por um `SemaphoreSlim`: chamadas simultâneas entram numa fila e rodam um por frame.

## 5. Alpha pré-multiplicado

Um `SubViewport` com fundo transparente devolve **cores pré-multiplicadas pelo alpha** ([godotengine/godot#99715](https://github.com/godotengine/godot/issues/99715), aberto e reproduzido até a 4.6). Desenhada com o material normal, uma textura assim tem as bordas semitransparentes mais escuras.

**Regra:** toda textura assada é desenhada com `BakeCompositor.BakedMaterial` (um `CanvasItemMaterial` com `BlendMode = PremultAlpha`), incluindo retratos e ícones na UI. Pixel art com alpha binário nem mostraria o problema, mas a regra vale para o dia em que entrar arte com borda suave. Se for exportar o personagem como PNG (miniatura, compartilhamento), a conversão para alpha normal é feita só nesse momento.

## 6. Custo

- **Memória:** `largura × altura × 4` bytes por personagem. Com o contrato atual (512×96) dá cerca de 192 KB, então 200 NPCs ocupam uns 38 MB.
- **Cache (ainda não implementado):** usar o hash do descriptor como chave, para NPCs gerados com a mesma receita compartilharem a textura.
- **Vazão:** um bake por frame. Carregar 200 NPCs de uma vez leva uns 3 s a 60 fps. Se isso incomodar, a otimização é assar vários personagens lado a lado num viewport maior, no mesmo frame.

## 7. Uso

```csharp
Contract contract = PaperDollJson.Load<Contract>("res://data/contract.json");
var catalog = new PieceCatalog();
var compositor = BakeCompositor.Create(contract, catalog);
AddChild(compositor);                                   // precisa estar na árvore para renderizar

CharacterDescriptor descriptor = PaperDollJson.Load<CharacterDescriptor>("res://demo/sample_character.json");
BakePlan plan = Resolver.Resolve(descriptor, contract, catalog);
Texture2D texture = await compositor.Bake(plan);

var sprite = new Sprite2D
{
    Texture = texture,
    Material = BakeCompositor.BakedMaterial,            // obrigatório: cores pré-multiplicadas
    Hframes = contract.Canvas.Frames,
};
```

## 8. Teste de fumaça

`test/smoke_test.tscn` confere:

- a ordem das camadas e o tamanho do bake;
- dois bakes simultâneos saem corretos e sem se misturar;
- um descriptor quebrado de várias formas ao mesmo tempo gera a lista completa de erros (11), e não só o primeiro;
- um decalque maior que a zona é recusado;
- JSON com campo faltando ou campo desconhecido é recusado;
- um print da cena demo.

O mesmo teste também exercita o editor do jogador (ver [editor-do-jogador.md](editor-do-jogador.md)).

Pela linha de comando: `godot --path . res://test/smoke_test.tscn`. O código de saída é 0 quando tudo passa e 1 na primeira falha.

## Próximos passos

- **Estampas (padrões tileáveis)** no canal B das roupas.
- **Gradient map** para pele e cabelo: o shade indexa uma rampa de cor em vez de multiplicar. A arte não muda, só o shader.
- **Cache por hash do descriptor** e bake de vários personagens por frame.

O editor do jogador está em [editor-do-jogador.md](editor-do-jogador.md).
