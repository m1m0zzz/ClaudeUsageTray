# ClaudeUsageTray

<img width="318" height="271" alt="image" src="https://github.com/user-attachments/assets/5c9fc7a4-d2c1-4523-ae85-d0c0e63f9170" />

<br />

<img width="191" height="42" alt="image" src="https://github.com/user-attachments/assets/b7a1cd55-1160-4683-83cc-0b82df462399" />


Claude の 5 時間枠・週間枠の使用率を Windows の通知領域に常時表示する常駐アプリ。

- トレイアイコンに **5 時間枠の使用率を数値で描画**（緑 <50 / 黄 50–80 / 赤 >80 / 100% は `!!`）
- ホバーで 5h・週間の使用率と回復時刻をツールチップ表示
- 左クリックで詳細パネル（5h / 週間 / Sonnet 別枠 / Opus 別枠 / 追加課金枠）
- 80% と 100% を跨いだときにトースト通知（エッジトリガ、枠のリセットで再武装）
- 外部 NuGet 依存なし
