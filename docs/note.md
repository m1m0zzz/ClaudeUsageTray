# ClaudeUsageTray

Claude の 5 時間枠・週間枠の使用率を Windows の通知領域に常時表示する常駐アプリ。

- トレイアイコンに **5 時間枠の使用率を数値で描画**（緑 <50 / 黄 50–80 / 赤 >80 / 100% は `!!`）
- ホバーで 5h・週間の使用率と回復時刻をツールチップ表示
- 左クリックで詳細パネル（5h / 週間 / Sonnet 別枠 / Opus 別枠 / 追加課金枠）
- 80% と 100% を跨いだときにトースト通知（エッジトリガ、枠のリセットで再武装）
- 外部 NuGet 依存なし

---

## 認証情報の探索

Claude Code CLI が保存した OAuth トークンを、以下の順で探します。

1. **Windows 側** `%USERPROFILE%\.claude\.credentials.json`
2. **WSL 側** `~/.claude/.credentials.json`（Windows 側が無効な場合のみ）

### 判定基準は「存在」ではなく「有効性」

Windows 側にファイルが残っていても `expiresAt` が過ぎていれば WSL 側に回します。
存在チェックで分岐すると、使わなくなった Windows 側の期限切れトークンを掴み続けて
フォールバックが一度も発動しません。

Windows 側が有効なら WSL には**一切触りません**（`wsl.exe` の起動コストを避けるため）。

### WSL の自動起動を避ける

`\\wsl$` への UNC アクセスも `wsl.exe` でのコマンド実行も、停止中のディストロを起動させます。
3 分おきにこれを行うと WSL が永久に落ちなくなるため、
`wsl.exe --list --running --quiet` で**起動中かを先に確認し、停止中なら読みに行きません**
（このコマンド自体はディストロを起動しません）。

`allowWslAutoStart` を `true` にすると、停止中でも起こして読みに行きます。

読み取りは `wsl.exe -d <distro> -- sh -c 'cat ~/.claude/.credentials.json'` です。
`~` の展開をシェルに任せることで、Linux 側のユーザー名をハードコードせずに済みます。
ログインシェル（`-l`）は profile 読み込みのぶん遅く MOTD が混ざる恐れがあるため使いません。

**注意**: `wsl.exe --list` 系の出力は UTF-16LE、コマンド実行時の出力は UTF-8 です。
`WslBridge` では呼び分けています。

---

## 通知のタイミング

閾値を**跨いだ瞬間に 1 回だけ**発火します（エッジトリガ）。3 分間隔のポーリングで
単純比較すると、80% 超が続く間ずっと通知が飛び続けるためです。
`resets_at` が変わる＝枠がリセットされたとみなして再武装します。

**起動直後の 1 回目は通知しません。** 起動時点で既に閾値を超えていた場合、それは
アイコンの色で分かるためです。設定の再読み込み直後も同様に抑制します
（`ThresholdNotifier.Reset()` で内部状態が消えるため、編集しただけで飛ぶのを防ぐ）。

抑制中でも `Evaluate()` は必ず呼んで内部の level を記録しています。
呼ばずに飛ばすと level が 0 のままになり、次回のポーリングで結局発火してしまいます。

この設計上、**アプリを閉じている間に閾値を跨いだぶんは通知されません**
（例: 75% で終了 → CLI で使い続ける → 85% で再起動、この場合は通知なし）。
アイコンには反映されるため、実用上の損失は「起動の瞬間の状態を知らせないこと」だけです。
プロセス再起動をまたいで通知したい場合は、通知状態の永続化が必要になります。

---

## アイコンの状態

| 状況 | 表示 |
|---|---|
| 取得成功 | 使用率の数値（緑 <50 / 黄 50–80 / 赤 >80 / 100% は `!!`） |
| 取得失敗だが直近の成功値がある | **同じ数値をグレーで表示**。ツールチップに「3時間前の値」 |
| 認証情報がどこにも無い／全ソースで 401 | `×`（ユーザーの操作が必要） |
| 直近値なし、または `maxStaleMinutes` 超過 | `?` |

WSL が停止している間は使用も発生していないため、`×` を出すより
「古い値であることが分かる表示」の方が実用的です。

**`×` はユーザーが行動すべき状態だけに使います。** WSL 停止中の 401 は
「再ログインが必要」ではなく「現役の認証情報を読めなかった」として扱い、
グレー表示にとどめます。この区別は `CredentialResolution.WslUnavailable` で判定しています。

---

## リポジトリ構成

```
.
├─ .gitignore
├─ .github/workflows/release.yml   手動実行のリリースワークフロー
├─ README.md
└─ src/
   ├─ ClaudeUsageTray.csproj
   └─ *.cs
```

---

## ビルド

```powershell
dotnet publish src/ClaudeUsageTray.csproj -c Release
# 出力: src\bin\Release\net9.0-windows\win-x64\publish\ClaudeUsageTray.exe
```

`.csproj` で `SelfContained` / `PublishSingleFile` を有効にしてあるため、単一 EXE
（.NET ランタイム同梱、約 70MB）が出ます。ランタイムを別途入れる前提なら
`.csproj` の `<SelfContained>` を `false` にしてください（数百 KB になります）。

開発中は `dotnet run --project src` で動きます。

### リリース

GitHub Actions の **Release** ワークフローを手動実行し、バージョン（例 `1.0.0`）を入力します。
既定では下書きとしてリリースが作られるので、内容を確認してから公開できます。

添付される資産は ZIP 1 点です。

| 資産 | 内容 |
|---|---|
| `ClaudeUsageTray-v1.0.0-win-x64.zip` | exe + README |

**exe は未署名です。** 初回起動時に SmartScreen の警告が出るので、
「詳細情報」→「実行」で進めてください。

---

## 起動前に必ず確認すること

このアプリは Anthropic の **非公開エンドポイント** `GET https://api.anthropic.com/api/oauth/usage`
を叩きます。公式 API ではないため、レスポンス形式は実測で確認してください。

```powershell
$c = Get-Content "$env:USERPROFILE\.claude\.credentials.json" -Raw | ConvertFrom-Json
$t = $c.claudeAiOauth.accessToken
Invoke-RestMethod -Uri "https://api.anthropic.com/api/oauth/usage" `
  -Headers @{ "Authorization" = "Bearer $t"; "anthropic-beta" = "oauth-2025-04-20" } |
  ConvertTo-Json -Depth 5
```

### `utilization` のスケール

**実測で確定済み: 0〜100 の百分率です。** `utilizationScale` は既定の `"percent"` のままで
問題ありません。`"fraction"` は将来 API 側の表記が変わった場合の逃げ道として残しています。

### 確認済みのレスポンス形状（Max プラン / 2026-07 時点）

```json
{
  "five_hour":  { "utilization": 94, "resets_at": "2026-07-23T12:10:00+00:00",
                  "limit_dollars": null, "used_dollars": null, "remaining_dollars": null },
  "seven_day":  { "utilization": 50, "resets_at": "2026-07-25T06:00:00+00:00" },
  "seven_day_sonnet": null,
  "seven_day_opus": null,
  "extra_usage": { "is_enabled": true, "monthly_limit": 4000, "used_credits": 1503,
                   "utilization": 37.57, "currency": "USD", "decimal_places": 2 },
  "limits": [ { "kind": "session", "percent": 94, "severity": "critical", ... } ],
  "spend":  { "used": { "amount_minor": 1503, "exponent": 2 }, "percent": 38, ... }
}
```

**`used_credits` / `monthly_limit` は最小通貨単位です。** `decimal_places: 2` なので
`1503` は $15.03、`4000` は $40.00 を意味します。そのまま表示すると 100 倍ずれます。

`seven_day_sonnet` / `seven_day_opus` は null で返るため、該当行は自動的に消えます。

レスポンスには `seven_day_oauth_apps` など用途不明のキーが複数含まれますが（すべて null）、
System.Text.Json は未知のプロパティを既定で無視するため、キーが増えても壊れません。

### `limits[]` について

`five_hour` / `seven_day` と同じ値を、`kind` / `severity` / `is_active` 付きの配列で
返す別系統の構造があります。本アプリは名前付きフィールドの方を使っていますが、
Anthropic が枠の種類を増やした場合は `limits[]` の方が追従しやすい構造です。
`severity`（`"critical"` / `"normal"`）はサーバー側の閾値判定なので、
自前の閾値の代わりに使うこともできます。

---

## 設定

`%APPDATA%\ClaudeUsageTray\config.json`

| キー | 既定値 | 説明 |
|---|---|---|
| `pollSeconds` | `180` | 更新間隔（秒）。**180 未満は自動的に 180 に引き上げられます** |
| `warnThresholdPercent` | `80` | 警告トーストの閾値 |
| `limitThresholdPercent` | `100` | 上限トーストの閾値 |
| `notifyFiveHour` | `true` | 5 時間枠のトースト |
| `notifySevenDay` | `true` | 週間枠のトースト |
| `runAtStartup` | `true` | Windows 起動時の自動起動 |
| `utilizationScale` | `"percent"` | `"percent"` または `"fraction"` |
| `logRawResponse` | `false` | 生レスポンスの書き出し |
| `credentialsPathOverride` | `""` | Windows 側 credentials.json の場所を明示する場合 |
| `useWslFallback` | `true` | Windows 側が無効なとき WSL 側を読む |
| `wslDistro` | `""` | 読みに行くディストロ名。空なら既定 |
| `allowWslAutoStart` | `false` | 停止中の WSL を起動してでも読む |
| `maxStaleMinutes` | `720` | 直近値をグレー表示で使い続ける上限（分） |

編集後はトレイ右クリック →「設定を再読み込み」で反映されます（再起動不要）。

---

## 設計上の判断

### トークンをリフレッシュしない

`refreshToken` を使って自前でトークンを更新すると、**Claude Code 本体が保持している
トークンを無効化してログインを壊す**恐れがあります。そのため本アプリは
`.credentials.json` を毎回読み直すだけで、書き込みは一切行いません。

401 が返った場合はエラーアイコン（グレーの `×`）を表示し、
`claude` で `/login` を実行するよう案内します。Claude Code 側でログインし直せば
次回のポーリングで自動的に復帰します。

### ポーリング間隔 180 秒

このエンドポイントは短間隔で叩くと 429 が張り付く報告があります。
設定で 180 秒未満にはできません。手動更新にも 60 秒のクールダウンがあります。

### アイコンのハンドルリーク対策

`Bitmap.GetHicon()` が返す HICON は GDI オブジェクトで GC 対象外です。
3 分おきに生成するため、`IconRenderer` では `Icon.FromHandle().Clone()` で
マネージドなコピーを作った直後に `DestroyIcon` を呼んでいます。
ここを省くと数日でハンドルが枯渇します。

### トースト通知に `ShowBalloonTip` を使用

外部依存（`CommunityToolkit.WinUI.Notifications` 等）を避けるため、
`NotifyIcon.ShowBalloonTip` を使っています。Windows 10/11 では通常のトーストとして
表示されますが、以下の制約があります。

- ボタンやカスタムレイアウトは使えない
- **集中モード（応答不可）が有効なときは抑制される**
- 通知履歴に残らない場合がある

これらが問題になる場合は、AUMID 登録を伴う本来のトースト API への移行が必要です。

---

## 既知の制約

- **Windows 11 は新規のトレイアイコンを既定でオーバーフロー（`^`）に隠します。**
  常時表示にするには「設定 → 個人用設定 → タスクバー → その他のシステム トレイ アイコン」
  で本アプリをオンにしてください。プログラムから固定する正攻法はありません。
- 非公開エンドポイントに依存しているため、`anthropic-beta` ヘッダの値や
  レスポンス形式が変わると動作しなくなります。
- 16px のアイコンに 3 桁は入らないため、100% は `!!` で表示します。
- **Claude Desktop（GUI アプリ）は `.credentials.json` を更新しません。** トークンを
  リフレッシュするのは Claude Code CLI だけなので、CLI を使っていない環境では
  トークンが期限切れのまま放置されます。
- WSL 側を使う構成では、**WSL が停止している間は値が更新されません**。
  その間はグレー表示になり、経過時間がツールチップに出ます。

---

## ファイル構成

| ファイル | 役割 |
|---|---|
| `src/Program.cs` | エントリポイント、多重起動防止、DPI 設定 |
| `src/AppConfig.cs` | 設定の読み書き、`utilization` の正規化 |
| `src/UsageModels.cs` | レスポンス DTO とスナップショット |
| `src/CredentialStore.cs` | Windows / WSL からのトークン探索（読み取り専用） |
| `src/WslBridge.cs` | wsl.exe 経由の起動確認とファイル読み取り |
| `src/UsageApiClient.cs` | エンドポイント呼び出しとエラー分類 |
| `src/IconRenderer.cs` | GDI+ でのアイコン描画と HICON 解放 |
| `src/ThemeHelper.cs` | ライト/ダークテーマの判定 |
| `src/StartupManager.cs` | `HKCU\...\Run` の制御 |
| `src/ThresholdNotifier.cs` | エッジトリガの閾値判定 |
| `src/PopupForm.cs` | 詳細パネル（自前描画） |
| `src/TrayContext.cs` | 全体のオーケストレーション |
