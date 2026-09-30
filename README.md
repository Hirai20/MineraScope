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
