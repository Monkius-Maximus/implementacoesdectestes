# Tutorial de uso

Passo a passo prático: abrir o projeto, montar um personagem no editor e adicionar uma peça de arte nova. Para entender **como** cada parte funciona por dentro, veja [compositor-de-bake.md](compositor-de-bake.md) e [editor-do-jogador.md](editor-do-jogador.md). Este documento é só o "como usar".

## 1. Abrir o projeto

1. Instale o [Godot 4.7 .NET](https://godotengine.org/download) (a versão com suporte a C#, não a padrão) e um SDK do .NET 8.
2. Abra o Godot, **Importar**, aponte para `paperdoll/godot/project.godot`.
3. Na primeira abertura, o Godot pede para importar os assets. Deixe terminar (é rápido, são só as peças de exemplo).
4. Aperte **F5** (ou o botão de play no canto superior direito). Isso abre a cena principal, `editor/character_editor.tscn`, com um personagem de exemplo já montado.

Se o F5 pedir para escolher uma cena principal, confirme que `project.godot` tem `run/main_scene="res://editor/character_editor.tscn"` (Projeto → Configurações do Projeto → aba Aplicativo → Executar).

## 2. Tour pela interface

A tela se divide em duas colunas:

- **Esquerda:** uma seção por slot (`hair_back`, `body`, `legs`, `torso`, `eyes`, `hair_front`, `hat`), de cima para baixo. Cada seção tem:
  - um seletor de peça no cabeçalho (`— nenhuma —` ou o id de uma peça, ex. `shirt_02`);
  - uma linha por região pintável, com o nome vindo do contrato (ex. "Primária", "Gola", "Tom de pele") e uma roda de cor ao lado;
  - se a peça tiver zona de decalque, as linhas de decalque já colocados e um botão **+ decalque**.
- **Direita:** a barra de ferramentas (nome + **Salvar**, seletor + **Carregar**, **Cores aleatórias**), o preview do personagem animado, e uma linha de status embaixo (fica vermelha quando o personagem atual é inválido).

Tudo que você muda na esquerda reflete no preview da direita assim que o bake termina — geralmente no mesmo frame ou no seguinte.

## 3. Montar um personagem do zero

1. Em cada slot, abra o seletor de peça e escolha uma. `body` precisa ter alguma peça marcada — as outras peças exigem a tag `body_humanoid`, que só o corpo fornece. Sem corpo, a linha de status mostra o erro e o preview continua mostrando o último estado válido.
2. Para cada região que aparecer, clique na roda de cor e escolha a cor. A mudança já é aplicada no personagem.
3. Se a peça tiver zona de decalque (por enquanto, as camisas de exemplo têm uma no peito), clique em **+ decalque**. Aparece uma linha com imagem, zona, `x`, `y`, escala, rotação (0/90/180/270°) e a caixa "espelhar". Os campos `x`/`y` não deixam você arrastar o decalque para fora da zona — o limite é recalculado sozinho quando você muda escala ou rotação.
4. Para remover uma peça, volte no seletor do slot e escolha `— nenhuma —`. Para remover um decalque, use o botão de remover na própria linha dele.
5. **Cores aleatórias** sorteia uma cor nova para toda região de toda peça equipada — bom para ver rápido se as combinações extremas (branco, preto, saturado) ficam legíveis.

## 4. Salvar e carregar

- Digite um nome no campo da barra de ferramentas (só `a-z`, `0-9`, `_` e `-`) e clique **Salvar**. O personagem vai para `user://characters/<nome>.json` — no seu computador isso fica em `%APPDATA%/Godot/app_userdata/PaperDoll/characters/` (Windows) ou `~/.local/share/godot/app_userdata/PaperDoll/characters/` (Linux/macOS).
- O seletor ao lado de **Carregar** lista os personagens salvos. Escolha um e clique **Carregar**. Um arquivo inválido (editado à mão e quebrado, por exemplo) é recusado sem mudar o personagem atual na tela.

## 5. Ver a montagem sem o editor (`demo.tscn`)

`demo/demo.tscn` (F6 com essa cena aberta) assa o `sample_character.json` e mais 4 variações de cor lado a lado, sem interface — útil para ver várias combinações de uma vez ou testar o compositor isoladamente. **Espaço** ou **Enter** gera novas variações aleatórias.

## 6. Adicionar uma peça de arte nova

Isso é o que substitui as peças de exemplo (`body_01`, `shirt_02` etc.) pela arte real do seu jogo.

1. **Desenhe no Aseprite**, em cima do template do slot (canvas `64×96`, 8 frames — ver `contract.json`). Camadas de topo: `shade` (tons de cinza, silhueta no alpha) e `mask` (regiões pintadas em R/G/B, conforme a categoria do slot — veja a tabela na seção 2 do README). Guias começam com `_` e não são exportadas.
2. Ao lado do `.aseprite`, crie `<piece_id>.meta.json` com as regiões usadas e suas cores padrão (veja o exemplo na seção 5 do README).
3. Exporte:
   ```bash
   ASEPRITE="/caminho/para/aseprite" python tools/export_aseprite.py --src art/src --out godot/assets
   ```
   > O exportador ainda não foi testado contra um Aseprite de verdade. Rode numa peça só primeiro e confira o PNG resultante antes de exportar tudo.
4. Valide:
   ```bash
   python tools/validate_assets.py --contract godot/data/contract.json --assets godot/assets
   ```
   Corrija o que o validador apontar antes de seguir — ele barra os mesmos problemas que o Resolver barraria em runtime, só que mais cedo e com a peça exata.
5. Volte para o Godot. Se o editor já estava aberto, feche e abra de novo (ou apenas rode a cena de novo com F5) para o Godot reimportar os PNGs novos. A peça aparece automaticamente no seletor do slot correspondente — nenhum código muda, porque o seletor lê o diretório `res://assets/<slot>/` na hora.

Para tirar as peças de exemplo depois de ter arte real equivalente em todo slot, basta apagar as pastas em `godot/assets/<slot>/` — o editor para de listá-las sozinho.

## 7. Conferir que nada quebrou

```bash
xvfb-run godot --path paperdoll/godot --headless --import   # reimporta tudo, uma vez
xvfb-run godot --path paperdoll/godot res://test/smoke_test.tscn
```

Sai com código `0` e a mensagem `OK — imagens em ...` quando tudo passa, `1` na primeira falha. As imagens geradas (bake de exemplo, demo, editor) ficam em `user://smoke_test/`, no mesmo lugar dos personagens salvos.

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| Linha de status vermelha ao abrir | Descriptor salvo referencia uma peça que não existe mais (renomeada/apagada) |
| Peça nova não aparece no seletor | Faltou reimportar (feche e abra o projeto, ou `--headless --import`) ou a pasta está fora de `godot/assets/<slot>/` |
| `validate_assets.py` acusa "pintado fora da declaração" | Um canal da máscara tem pixel não-zero sem a região correspondente em `meta.json` |
| Decalque não entra na peça | A peça não tem `decal_zones` no `meta.json`, então o botão **+ decalque** fica desabilitado |
| Cor de roupa "vazando" nas bordas em pixel art | Máscara com anti-aliasing numa categoria sem gradiente (`clothing`, `eyes`) — repinte a borda com 0 ou 255 puro |
