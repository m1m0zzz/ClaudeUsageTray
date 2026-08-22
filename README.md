
# <img width="24" height="24" alt="ClaudeUsageTray Icon" src="./icon.png" /> ClaudeUsageTray

<img width="340" height="341" alt="image" src="https://github.com/user-attachments/assets/53446e9d-173f-461e-980f-44f465faf23a" />

Claude Code の使用率を Windows の通知領域に常時表示する常駐アプリです。

- トレイアイコンに 5 時間枠の使用率を表示
- ホバーで 5 時間枠・週間枠の使用率と回復時刻を表示
- 左クリックで 5 時間枠、週間枠、モデル別枠、追加課金枠の詳細を表示
- 設定した使用率を跨いだときに通知
- Windows と WSL の Claude Code 認証情報に対応
- 単一 EXE で動作し、外部 NuGet パッケージへの依存なし

> [!WARNING]
> このアプリは Anthropic の非公開エンドポイント
> `GET https://api.anthropic.com/api/oauth/usage` を利用します。
> API の変更や廃止により、予告なく動作しなくなる可能性があります。

## 必要なもの

- Windows x64
- Windows または WSL にインストールされ、ログイン済みの Claude Code CLI

Claude Desktop のログイン情報は利用しません。Claude Code が作成する
`.claude/.credentials.json` が必要です。

## インストール

1. [GitHub Releases](https://github.com/m1m0zzz/ClaudeUsageTray/releases) から
   `ClaudeUsageTray-v<version>-win-x64.zip` をダウンロードします。
2. ZIP を任意のフォルダーへ展開します。
3. `ClaudeUsageTray.exe` を実行します。

インストーラーはありません。EXE は自己完結型のため、.NET ランタイムを別途入れる必要は
ありません。EXE は未署名なので、初回起動時に SmartScreen が表示された場合は
「詳細情報」→「実行」を選択してください。

既定では Windows へのサインイン時に自動起動します。トレイアイコンを右クリックし、
「Windows 起動時に開始」のチェックを外すと無効にできます。

## 使い方

- **左クリック**: 詳細パネルを開く／閉じる
- **ホバー**: 5 時間枠・週間枠の使用率と回復時刻を確認
- **右クリック**: 手動更新、設定、自動起動、終了
- **詳細パネルのベル**: 使用率通知を ON／OFF

詳細パネルには、API から値が返された項目だけが表示されます。

- 5 時間枠
- 週間枠（全体）
- 週間枠（Sonnet）
- 週間枠（Opus）
- 追加課金枠

手動更新には 60 秒のクールダウンがあります。

### アイコンの見方

| 状態 | 表示 |
|---|---|
| 取得成功 | 5 時間枠の使用率（四捨五入）。緑は 0〜49、黄は 50〜80、赤は 81〜99 |
| 100% | `!!` |
| 取得失敗・直近の成功値あり | 直近の使用率をグレーで表示。ツールチップに経過時間を表示 |
| 再ログインが必要 | `×` |
| 値がない、または直近値が古すぎる | `?` |

Windows 11 では、新しいトレイアイコンが `^` の中に隠れることがあります。常時表示する場合は、
「設定 → 個人用設定 → タスクバー → その他のシステム トレイ アイコン」で
ClaudeUsageTray を有効にしてください。

## 認証情報

Claude Code CLI が保存した OAuth アクセストークンを、次の順で読み取ります。

1. Windows の `%USERPROFILE%\.claude\.credentials.json`
2. WSL の `~/.claude/.credentials.json`

Windows 側のファイルが存在していても、`expiresAt` が期限切れなら WSL 側を試します。
Windows 側の認証情報が有効な場合は、WSL にアクセスしません。

認証情報は読み取り専用です。`refreshToken` を使った更新やファイルへの書き込みは行いません。
トークンが無効になった場合は、Claude Code で `/login` を実行してください。次回の更新時に
新しい認証情報が読み込まれます。

### WSL の扱い

既定では、対象の WSL ディストリビューションが起動中のときだけ認証情報を読み取ります。
定期更新のたびに停止中の WSL を起動しないためです。停止中は直近の成功値をグレーで表示し、
利用可能な直近値がなければ `?` を表示します。

停止中の WSL も自動起動する場合は、設定の `allowWslAutoStart` を `true` にしてください。
特定のディストリビューションを使う場合は `wslDistro` に名前を指定します。

## 通知

既定では、5 時間枠または週間枠の使用率が 80% と 100% の閾値に達したとき、
各レベルにつき 1 回通知します。同じレベルに留まっている間は繰り返しません。使用率が下がると
再び通知できる状態になります。

起動直後と設定の再読み込み直後は、すでに閾値以上でも通知しません。アプリを終了している間の
閾値超過も遡って通知されません。通知状態はプロセスをまたいで保存されないためです。

通知には WinForms の `NotifyIcon.ShowBalloonTip` を使用しています。集中モードでは抑制され、
通知履歴に残らない場合があります。

## 設定

初回起動時に次のファイルが作成されます。

```text
%APPDATA%\ClaudeUsageTray\config.json
```

トレイアイコンを右クリックして「設定ファイルを開く」を選ぶと編集できます。保存後、
「設定を再読み込み」を選ぶと再起動せずに反映されます。通知の ON／OFF と自動起動は UI からも
変更でき、その場で設定ファイルへ保存されます。

| キー | 既定値 | 説明 |
|---|---:|---|
| `pollSeconds` | `180` | 自動更新の間隔（秒）。180〜3600 に補正 |
| `warnThresholdPercent` | `80` | 警告通知の閾値。1〜100 に補正 |
| `limitThresholdPercent` | `100` | 上限通知の閾値。1〜100 に補正 |
| `notificationsEnabled` | `true` | 使用率通知全体の ON／OFF |
| `notifyFiveHour` | `true` | 5 時間枠を通知対象にする |
| `notifySevenDay` | `true` | 週間枠を通知対象にする |
| `runAtStartup` | `true` | Windows サインイン時に起動する |
| `utilizationScale` | `"percent"` | API の使用率を `percent`（0〜100）または `fraction`（0〜1）として解釈 |
| `logRawResponse` | `false` | 成功時の API レスポンスを `last-response.json` に保存 |
| `credentialsPathOverride` | `""` | Windows 側の認証情報ファイルを別のパスにする |
| `useWslFallback` | `true` | Windows 側が使えない場合に WSL を試す |
| `wslDistro` | `""` | 使用する WSL ディストリビューション。空なら既定 |
| `allowWslAutoStart` | `false` | 認証情報の取得時に停止中の WSL を起動する |
| `maxStaleMinutes` | `720` | 直近の成功値をグレー表示する上限（分）。5〜10080 に補正 |

`warnThresholdPercent` が `limitThresholdPercent` より大きい場合は、読み込み時に両者を
入れ替えます。生レスポンスの保存先は
`%APPDATA%\ClaudeUsageTray\last-response.json` です。

## API レスポンスの扱い

現在の実装は、レスポンスの次のフィールドを使用します。

- `five_hour`
- `seven_day`
- `seven_day_sonnet`
- `seven_day_opus`
- `extra_usage`

各利用枠の `utilization` は既定で 0〜100 の百分率として扱います。API の表現が 0〜1 に
変わった場合に備えて、`utilizationScale: "fraction"` も選択できます。

`extra_usage.used_credits` と `extra_usage.monthly_limit` は最小通貨単位として扱い、
`decimal_places` に従って表示します。未知のフィールドは無視するため、レスポンスに項目が
追加されただけではエラーになりません。一方、利用中のフィールド名や認証方式が変わった場合は
アプリの更新が必要です。

## 実装上の補足

- 自動更新は既定で 180 秒間隔です。非公開 API のレート制限を避けるため、設定を
  180 秒未満にはできません。
- WSL の確認には `wsl.exe --list --running --quiet` を使います。`--list` の出力は
  UTF-16LE、WSL 内のファイル読み取り結果は UTF-8 として処理します。
- WSL の認証情報は `sh -c 'cat ~/.claude/.credentials.json'` 相当のコマンドで読みます。
  ログインシェルは起動しません。
- トレイアイコンは GDI+ で描画します。`Bitmap.GetHicon()` で得た HICON は複製後に
  `DestroyIcon` で解放し、定期更新によるネイティブハンドルのリークを防いでいます。
- API には Bearer トークンと `anthropic-beta: oauth-2025-04-20` を付けてアクセスします。

## 開発

.NET 9 SDK が必要です。

```powershell
dotnet run --project src
```

自己完結型の単一 EXE を発行するには、次を実行します。

```powershell
dotnet publish src/ClaudeUsageTray.csproj -c Release
```

出力先:

```text
src\bin\Release\net9.0-windows\win-x64\publish\ClaudeUsageTray.exe
```

フレームワーク依存で発行する場合は、`ClaudeUsageTray.csproj` の `SelfContained` を
`false` に変更してください。

### リリース

GitHub Actions の **Release** ワークフローを手動実行し、バージョンを入力します。
既定ではドラフトリリースが作成され、次の ZIP が添付されます。

```text
ClaudeUsageTray-v<version>-win-x64.zip
```

ZIP には `ClaudeUsageTray.exe` と `README.md` が含まれます。リポジトリ直下に
`LICENSE`、`LICENSE.md`、`LICENSE.txt` のいずれかがある場合は、それも同梱されます。

## 構成

| ファイル | 役割 |
|---|---|
| `src/Program.cs` | エントリーポイント、多重起動防止、DPI 設定 |
| `src/AppConfig.cs` | 設定の読み書きと値の正規化 |
| `src/UsageModels.cs` | API レスポンスと画面表示用データ |
| `src/CredentialStore.cs` | Windows／WSL の認証情報の探索 |
| `src/WslBridge.cs` | WSL の起動状態確認とファイル読み取り |
| `src/UsageApiClient.cs` | 使用率の取得とエラー分類 |
| `src/IconRenderer.cs` | トレイアイコンの描画とネイティブハンドルの解放 |
| `src/SvgPathIcon.cs` | 埋め込み SVG アイコンの描画 |
| `icon.png` / `src/Assets/icon.ico` | EXE アイコンの原画像／Windows アイコン |
| `src/ThemeHelper.cs` | Windows のライト／ダークテーマ判定 |
| `src/StartupManager.cs` | `HKCU\...\Run` による自動起動の制御 |
| `src/ThresholdNotifier.cs` | 閾値を跨いだときの通知判定 |
| `src/PopupForm.cs` | 詳細パネル |
| `src/TrayContext.cs` | 取得、表示、操作のオーケストレーション |

## 既知の制約

- 非公開 API の仕様変更やレート制限の影響を受けます。
- 16 px のアイコンに 3 桁を表示できないため、100% は `!!` になります。
- Claude Desktop だけを使っている環境では、Claude Code の認証情報は更新されません。
- WSL 側の認証情報を使い、`allowWslAutoStart` が `false` の場合、WSL の停止中は更新されません。
- 通知にはボタンやカスタムレイアウトを追加できません。
