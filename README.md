# ANIMUS

Aplicativo desktop em **C# + XAML (Avalonia 11)** — roda em Windows e Linux com o mesmo código.

| Camada | Onde fica |
| --- | --- |
| Lógica e comportamento (C#) | `ViewModels/`, `Services/`, `Models/`, `Common/` |
| Aparência e estrutura (XAML) | `Views/`, `Styles/Theme.axaml`, `App.axaml` |
| Imagens (logo e fundos) | `Assets/` |
| Fontes embutidas (Kanit, Poppins, Chakra Petch, Rajdhani — OFL) | `Assets/Fonts/` |

## Telas

- **Login** — usuário + senha. Contas de fábrica: `kwvn` / `kwvn` e `saluty` / `saluty`.
- **Dashboard** — "Bem-vindo" com o nome do usuário logado.
- **23 DOX** — página própria no menu lateral (ver abaixo).
- **23 HOT** — página criada, ainda sem conteúdo definido (ver abaixo).
- **Configurações** — quatro abas:
  - **Conta** — troca a senha do usuário logado.
  - **Aparência** — cor de destaque (paleta + seletor livre), fonte (8 opções), tamanho do
    texto, opacidade dos painéis, quanto do fundo aparece, arredondamento das caixas,
    espaçamento interno, tamanho das abas/botões, e efeitos (sombras, animações). Tudo em
    sliders/opções, aplicado na hora e salvo sozinho.
  - **Fundo** — a única imagem de fundo do app.
  - **Notificações** — quando o app deve avisar (processo concluído/falho, config salva) + teste.

O menu lateral tem **Dashboard · 23 DOX · 23 HOT · Configurações**.

Cada página rola por dentro (o `ScrollViewer` fica dentro da página, não no `ShellView`):
assim a página tem altura definida e listas grandes conseguem virtualizar.

Tudo em Aparência é aplicado na hora e salvo sozinho — não tem botão de salvar.

Para adicionar um fundo novo, basta jogar o arquivo (`bg5.png`, por exemplo) em `Assets/` e
recompilar — ele aparece sozinho na aba Fundo.

### 23 DOX

Cinco consultas, cada uma dá GET numa URL. O JSON que volta é montado em blocos legíveis
(campos vazios são omitidos), cada campo copia ao clicar, e há um botão **Copiar tudo (TXT)**
que gera `RÓTULO: valor` linha a linha.

As URLs ficam em [`Services/DoxCatalog.cs`](Services/DoxCatalog.cs). O worker usa o formato
`{base}/{TOKEN}/{recurso}/{valor}` — **troque o `Token` por um válido**; o do `rules.txt`
antigo é recusado (o worker responde `token_invalido`). O pipeline (GET → parse → tela →
copiar) já funciona; só depende do token/rotas certos.

#### Filtro do resultado

O botão **Filtros**, no cabeçalho do resultado, abre um menu suspenso — ele não ocupa altura da
lista. O botão fica destacado quando algum critério está ligado, e **nem aparece** na consulta
por CPF (volta uma pessoa só, não há o que peneirar — a lista fica em
[`DoxViewModel.NoFilterQueries`](ViewModels/DoxViewModel.cs)) nem quando não há nada peneirável.

Dentro do menu, **cada filtro só aparece se o resultado tiver aquele dado** — nome mostra
estado/idade/gênero, telefone mostra estado e WhatsApp, placa não mostra nenhum. Não precisa
configurar nada por tipo de consulta: as opções saem do que a fonte devolveu.

- **Estado** — a cidade vem como `CIDADE-UF` (`ARAGUAINA-TO`), então a sigla sai do sufixo.
  Quando a cidade não traz, cai para o campo `Uf`/`Estado`, depois para a faixa do **CEP** e por
  último para o **DDD** ([`Services/BrazilRegions.cs`](Services/BrazilRegions.cs)).
- **Idade** (de/até, calculada da data de nascimento), **gênero** (todos/homem/mulher),
  **só com WhatsApp** e uma **busca livre** em qualquer valor.
- O estado só aparece se o resultado tiver mais de um — filtrar por um valor só não filtra nada.

Como o filtro decide: **registro** (a pessoa/empresa/veículo) que não bate sai inteiro; dentro
de um registro que ficou, o sub-bloco que *contradiz* o filtro some (endereço de outro estado,
telefone sem WhatsApp). Bloco que não tem aquele dado é neutro e continua na tela — o cabeçalho
da pessoa nunca some por causa de um filtro de endereço.

O **Copiar tudo (TXT)** copia o que está na tela, já filtrado.

#### Por que não trava com muito resultado

Consulta por nome pode voltar com centenas de pessoas (400 pessoas ≈ 460 KB de JSON,
4.000 blocos, 24.000 campos). O que segura isso:

- **Nada pesado na thread da interface** — download, parse do JSON, montagem dos blocos e o
  texto do "copiar tudo" rodam em `Task.Run`. A tela continua respondendo enquanto isso.
- **Lista achatada e virtualizada** — a árvore de blocos vira uma lista única
  ([`DoxBlockViewModel`](ViewModels/DoxFieldViewModel.cs)) num `ItemsControl` com
  `VirtualizingStackPanel`: existem ~30 controles vivos na tela, não 24.000. Medido com
  400 pessoas: 4 blocos realizados de 4.001, ~90 ms até aparecer.
- **Entrega de uma vez** — a lista inteira é trocada num aviso só, em vez de milhares de
  inserções uma a uma.
- **Tetos de segurança** (em [`Services/DoxService.cs`](Services/DoxService.cs)): 32 MB de
  resposta, 40.000 campos, 400 itens por lista, 8 níveis de aninhamento e 400 caracteres por
  valor. Se algo for cortado, a tela avisa em vez de engasgar.
- **Botão Cancelar** enquanto a consulta está em andamento.
- **Filtro sem engasgo** — os critérios esperam 140 ms depois da última tecla antes de refazer
  a lista. Medido com 400 pessoas (1.601 blocos): ~30 ms de trabalho para refiltrar.

O campo `owner` que a fonte devolve (a conta do token) não vira campo na tela nem no TXT.

### 23 HOT

Aba criada, ainda vazia — falta definir o que ela faz. Os arquivos são
[`ViewModels/HotViewModel.cs`](ViewModels/HotViewModel.cs) e
[`Views/HotView.axaml`](Views/HotView.axaml).

### Notificar quando um processo terminar

Os processos ainda vão ser adicionados. Quando forem, basta chamar:

```csharp
notifications.ProcessFinished("Backup", "Concluído em 3s.");
notifications.ProcessFailed("Backup", "Falhou: sem conexão.");
```

O serviço já respeita as preferências da aba Notificações.

## Onde ficam os dados

Senhas (hash PBKDF2-SHA256 com salt), tema, fonte, tamanho do texto, intensidade do fundo e
preferências de notificação ficam em:

- Linux: `~/.config/ANIMUS/config.json`
- Windows: `%APPDATA%\ANIMUS\config.json`

Apagar esse arquivo devolve as senhas de fábrica.

## Rodar em desenvolvimento

```bash
dotnet run
```

## Gerar o executável

```bash
# Linux (binário único, sem precisar de .NET instalado na máquina)
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o bin/publish/linux-x64

# Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o bin/publish/win-x64
```

Saída: `bin/publish/linux-x64/ANIMUS` e `bin/publish/win-x64/ANIMUS.exe`.
