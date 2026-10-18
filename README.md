# NBoardLocalGameServer

NBoardプロトコル対応の思考エンジン2つ（Edax、ntestなど）をローカルの子プロセスとして起動し、リバーシ/オセロの対局を自動で行う.NET 10のコンソールアプリです。持ち時間の管理、GGF形式の棋譜記録、勝敗・Elo差・統計的有意性の集計をします。

名前に「サーバー」とありますが、ソケットは使いません。エンジンが標準入出力でつながる、NBoardプロトコルのホスト役という意味です。

## ビルドと実行

```
dotnet build
dotnet run -- --config examples/match.example.json
```

マッチの設定は`--config`のJSON1つに全部書けます。同じ項目をコマンドライン引数でも指定した場合は、コマンドライン引数が優先されます（コマンドライン引数 > 設定ファイル > 既定値）。

## 設定ファイル

```jsonc
{
  "MatchMode": "Synchro",            // "Normal" | "Synchro"
  "SessionMode": "StatefulEngine",   // "StatefulEngine" | "StatelessEngine"
  "SwapPlayer": true,
  "UseSamePositionWhenSwapPlayer": true,
  "OpeningBookPath": "data/books/xot-500.book",
  "ShuffleBook": false,

  // 以下は任意（コマンドライン引数でも指定できる）
  "Matches": 100,
  "Sessions": 4,
  "TimeControl": "00:05:00/00:00:10/00:00:00",
  "TimeControlFirst": null,
  "TimeControlSecond": null,
  "First":  "players/edax.json",     // playerファイルのパス、または
  "Second": { "Path": "engine/ntest/NTest-x86.exe", "Arguments": "", "WorkDir": "engine/ntest/",
              "InitialCommands": [], "Name": "ntest" },   // インラインで書く
  "Stats":  "out/stats.json",
  "Record": "out/record.ggf"
}
```

| 項目 | 内容 |
| --- | --- |
| `MatchMode` | `Normal`は1マッチ=1局。`Synchro`はGGSのSynchro Match準拠で、同じ局面から手番を入れ替えた2局を1マッチとし、2局の合計石差でマッチの勝敗を決めます。`Synchro`のときは`SwapPlayer`と`UseSamePositionWhenSwapPlayer`は無視されます |
| `SessionMode` | `StatefulEngine`は指した手(`move`)だけを送り、局面と残り時間はエンジンが持ちます。`StatelessEngine`は毎手`set game`で局面と時計を送り直します |
| `SwapPlayer` | 1局ごとに先手後手を入れ替えます |
| `UseSamePositionWhenSwapPlayer` | 入れ替えた局を同じ局面で行うかどうか |
| `OpeningBookPath` / `ShuffleBook` | 開始局面集とそのシャッフル |

### playerの設定

`First`/`Second`は、次のどちらの形式でも書けます。

```json
{
  "Path": "engine/Edax/wEdax-x86-64-v4.exe",
  "Arguments": "-nboard -n 8 -move-time 1",
  "WorkDir": "engine/Edax/",
  "InitialCommands": ["set depth 60"],
  "Name": "Edax"
}
```

`InitialCommands`はNBoardのハンドシェイク直後にエンジンへ送られます。探索深さやハッシュサイズの設定に使います。`Name`は任意で、統計や結果に出す表示名です。省略するとエンジンが自己申告する名前になります。相対パスは実行時のカレントディレクトリからの相対です。

### 持ち時間

`ini/inc/extra`の3つをすべて書きます。GGFの`BT[]`/`WT[]`と同じ形式です。各項目は`HH:MM:SS[,N<手数>]`です。

- `ini`は初期持ち時間です。`N`を付けると、時間切れでも負けになりません。
- `inc`は1手ごとの加算時間です。`N`を付けると、Fischer式ではなくBronstein式になります。
- `extra`は初期持ち時間を使い切ったあとに1回与えられる追加時間（秒読み）です。`N`を付けると、加算ではなく置き換えになります。

例:

- `00:05:00/00:00:10/00:00:00`は、5分に1手ごと10秒のFischer加算で、追加時間なしです。
- `00:25:00/00:00:00/00:01:00,N1`は、25分のあとに1手1分の秒読みです。

### 開始局面集（book）

1行が`<64文字の盤面> <手番>`の形式で、`*`が黒、`O`が白、`-`が空きマスです。`tools/gen_book.py`で生成できます。`tools/*.book`は`.gitignore`で管理外にしています。

## コマンドライン引数

| 引数 | 内容 |
| --- | --- |
| `--config` | マッチ設定JSON（必須） |
| `--first` / `--second` | playerファイルのパス |
| `--matches`, `-nm` | マッチ数 |
| `--sessions`, `-ns` | 同時対局数。既定はCPUの論理プロセッサ数 |
| `--stats`, `-s` | 統計JSONの出力先 |
| `--record`, `-r` | GGF棋譜の出力先 |
| `--time`, `-t` | 両者共通の持ち時間 |
| `--time-first`, `-tf` / `--time-second`, `-ts` | 片方だけの持ち時間（`--time`より優先） |
| `--output-format`, `-o` | `text`（既定）または`json` |

`First`/`Second`、`Matches`、`Stats`は、設定ファイルかコマンドライン引数のどちらかで必ず指定してください。

## スクリプトやLLMから使う

`--output-format json`を付けると、進行ログはstderrに出ます。stdoutにはJSONオブジェクトが1つだけ出ます。設定エラーのときも`Status: "Error"`と`Message`を持つJSONが出ます。

```
dotnet run -- --config examples/match.example.json -o json > result.json
```

出力の主な項目は次のとおりです。

- `Status`: `Completed` / `Cancelled` / `FailedToStart` / `Error`
- `CompletedGames`、`TotalGames`
- `Players`: プレイヤーごとの勝敗数、得点率、`EloDiff`、`EloDiffMargin95`（95%信頼区間の半幅）、`SignificanceZ`、`ConfidenceLevel`など
- `Match`: Synchroのときだけ。マッチ単位の勝敗とElo差
- `StatsPath`、`RecordPath`、`StartedAt`、`FinishedAt`

終了コードは、0が完了、1が設定・引数エラーまたは開始失敗、2が中断です。Ctrl+Cで実行中の対局を打ち切ると、そこまでの統計を保存して終了します（終了コード2）。

## 出力ファイル

- `--stats`のJSON: プレイヤーごとの統計。Synchroのときはマッチ統計も入ります。100局ごとと終了時に書き出します。
- `--record`のGGF: 対局ごとの棋譜
- `log.txt`: エンジンとやり取りした生の標準入出力。エンジンが異常な動きをしたときの調査に使います。

## テスト

```
dotnet test NBoardLocalGameServer.slnx
```

`DummyEngine`は、ランダムに指すだけのNBoardエンジンです。これを使うので、実エンジンなしで同時対局の集計を検証できます。

## 構成

| ファイル | 役割 |
| --- | --- |
| `GameServer.cs` | マッチ全体の進行と同時対局の管理 |
| `GameSession.cs` | 1局の進行 |
| `GameClock.cs` | 持ち時間の管理 |
| `Engine/` | エンジンプロセスとNBoardプロトコル |
| `Reversi/` | ビットボードによる盤面とルール |
| `PlayerStats.cs` / `MatchStats.cs` / `Statistics.cs` | 勝率、Elo差、有意性の計算 |
