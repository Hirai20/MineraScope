# MineraScope

MineraScope is a Windows Forms application for mineral-related data processing and analysis.

## Development

- OS: Windows
- SDK: .NET 10
- Solution: `MineraScope.sln`
- App project: `MineraScope/MineraScope.csproj`

Build:

```powershell
dotnet build MineraScope.sln
```

Run:

```powershell
dotnet run --project MineraScope\MineraScope.csproj
```

## GitHub Setup

After creating a GitHub repository, initialize Git locally, commit the current files, and push to `main`.

```powershell
git init
git add .
git commit -m "Initial commit"
git branch -M main
git remote add origin https://github.com/<your-account>/MineraScope.git
git push -u origin main
```

## 分類モデルの学習率

<!-- 260930Codex: Document the ordinary default and compatibility rule beside the technical procedures. -->
通常のモデル作成では、分類モデルの標準学習率は `0.0001` です。学習結果の `trainingResults.json` に `settings.classificationLearningRate` として記録します。学習率の項目がない過去の設定JSONは、当時の条件を保つため `0.001` として読み込みます。回帰モデルの学習率は `0.001` です。

## 同名で上書きしたモデルの再読込

<!-- 260930Codex: Verify the selected model at operation start, not on every map block. -->
単発の分類開始時とマップ作成開始時に、使用する分類モデルの保存内容を確認します。同じ名前・保存先でも重みや付随設定が変わっていれば読み直し、変更がなければ読み込み済みのモデルを再利用します。マップは開始時に一度確認して全ブロックで同じ読み込み済みモデルを使用します。複数スペクトルの一括解析は操作ごとに作るサービスでモデルを読み込み、その操作内では再利用します。定量モデルも使用する保存内容を確認します。作成済みのマップや表示結果は自動で再計算しません。

## モデル作成の設定と分類＋OCN

<!-- 261021Codex: 選別した通常機能の挙動だけを公開版へ記載する。 -->
モデル作成画面で「分類」または「分類＋OCN」を選択できます。分類＋OCNは分類モデルを学習した後、同じ採用入力から未学習判定器を学習して1つのモデルとして保存します。分類候補と鉱物別確率には分類モデルの出力を使い、未学習検知に保存済みOCN判定器を自動で使います。未学習検知OFFも引き続き利用できます。OCNだけが学習できない場合は理由を通知して分類モデルと作成できた定量モデルを保存します。中止や入力変更では既存モデルを保持します。

新しい通常モデルの未知判定の距離倍率は1.0です。保存済みモデルの閾値は変更しません。カーボン蒸着膜厚は入力値に固定し、通常画面からの新規生成で膜厚のランダム化は行いません。詳細設定の高さはDesignerのパネル寸法と余白に従います。

## 複数起動での生成と学習

<!-- 261021Codex: 読み取り共有と生成排他の運用境界を明記する。 -->
別のMineraScopeでスペクトルを生成している間も、生成完了が管理ファイルに記録されたデータで学習できます。生成中や未完了のデータは学習に使いません。学習準備はpoolの管理状態を書き換えず、生成同士は引き続き排他制御します。同じモデル保存先への同時作成も拒否します。
