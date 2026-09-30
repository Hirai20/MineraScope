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
