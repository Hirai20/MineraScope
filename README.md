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

<!-- 261106Codex: 公開版も改変しないNIST公式版の直接起動へ移す。 -->
<!-- 261030Claude: DTSA-IIは同梱せず、NIST公式版を改変せずに使う。 -->
## DTSA-II

スペクトルの生成にはNISTのDTSA-IIを使います。DTSA-IIはこのリポジトリに含まれていないので、[NISTの配布ページ](https://www.cstl.nist.gov/div837/837.02/epq/dtsa2/dtsa2home.html)から公式版を入手してインストールしてください。MineraScopeはDTSA-IIのファイルを変更しません。

- 推奨版はNIST DTSA-II Polaris 2026-09-10（EPQ 15.1.57）です。Oberon 2024-06-11でも生成できることを確認しています。
- 起動時に`%LOCALAPPDATA%\NIST\NIST DTSA-II *`を探し、フォルダー名の日付が最も新しい版を「DTSA-IIパス」に表示します。保存済みのパスが使える場合はそちらを優先します。別の場所に入れた場合や別の版を使う場合は、この欄を変更してください。
- 生成時はDTSA-IIのGUIを開かず、同梱のJava（`java-runtime\bin\java.exe`）でJythonを直接起動して、DTSA-IIのjarを読み込みます。DTSA-IIのデータベースは使いません。
- Jythonのキャッシュは`%LOCALAPPDATA%\MineraScope\JythonCache`に作ります。
- 生成に使ったDTSA-IIのフォルダーとEPQの版は、生成ごとのログ（`Documents\MineraScope\Logs\simulate-*.log`）に記録します。

<!-- 261106Codex: 公開版の実行順・原子的保存・中止・CPU計測の規則を説明する。 -->
<!-- 261103Codex: バッチをまたぐ実行上限と、完了バッチの保存・中止時の扱いを説明する。 -->
生成ジョブは鉱物順に投入し、全鉱物を通して画面の「並列数」まで同時実行します。先の鉱物に未完了ジョブがあっても、空いた枠から次の鉱物を始めます。ジョブの分割本数・組成・測定条件・条件キー・pool構成は従来どおりです。1鉱物の全ジョブが終了すると、その鉱物の結果を保存します。保存は1バッチずつ行い、保存中もほかのジョブを実行できます。完了順によっては、後の鉱物を先に保存します。

中止すると新しいジョブの投入を止め、起動済みプロセスを停止して保存済みファイルを回収します。開始済みバッチの保存済み分を検証してCompletedにし、未生成分と未開始バッチはPendingに残します。保存に失敗した場合も、実行中ジョブを停止・回収してからエラーを返します。watchdogと起動時の再試行条件、起動間隔（2秒）は維持しています。公式版の直接起動ではDerbyを使いませんが、起動間隔の変更は別途測定して判断します。

<!-- 261109Codex: 公開版にも中止時の停止・回収と集計省略を反映する。 -->
中止時は「停止・保存済みデータ回収中」と表示し、停止要求の処理と保存・回収をバックグラウンドで進めます。回収後は全鉱物の完了・不足件数の再集計とジョブ別のキャンセルログ表示を省き、実行ボタンを戻します。通常終了後の集計中も中止できます。Javaの終了と保存・回収が完了するまでは、同じデータへの重複実行を防ぐため次の生成を開始できません。詳細な試行記録は引き続きrunログに残します。

runログには5秒ごとのJavaプロセス数とCPU使用率、最後に起動回数と平均CPU使用率も記録します。CPU使用率は、このrunで起動したJavaのCPU時間を経過時間と論理CPU数で割った値です。PC全体のCPU使用率とは区別してください。比較生成の手順は[テスト手順](MineraScope.Tests/README.md#シミュレーションの実行順と比較生成)を参照してください。

<!-- 261106Codex: 公開版の検出器登録と移行規則を説明する。 -->
<!-- 261103Claude: 検出器の値は検出器名で一覧から引く。 -->
## 検出器

スペクトルの生成に使う検出器（SDD）の値は、検出器名ごとに`%LOCALAPPDATA%\MineraScope\DetectorProfiles.json`へ登録します。生成・モデル作成は、画面の「検出器名」と完全に一致する名前の検出器の値を使います。GUIなしの生成と基準値の採取も同じ規則で引きます。

<!-- 261105Codex: 登録一覧だけで基本項目の欠落を拒否し、任意項目と旧形式の読込を区別する。 -->

- 登録されていない名前では、処理を始めずに理由を表示します。既定値やほかの検出器の値で代用しません。
- 名前は自由な文字列で、書式は解釈しません。空の名前、前後の空白、改行などの制御文字は使えません。大文字・小文字か全角・半角だけが違う名前は、別の検出器として登録できません。名前はファイル名やフォルダー名に使いません。
- 一覧のファイルが無いときは、起動時に`GeneratorFormSettings.json`の検出器を、その名前と値のまま登録します。
- 登録画面はまだ無いので、当面は`DetectorProfiles.json`を直接編集します。項目名は大文字・小文字を区別し、知らない項目や基本項目の欠落があるとファイルを読み込みません（有効な退避コピー`.bak`があれば理由を通知してそちらを使います）。値は読み込み時と生成時に検査し、範囲外の値を既定値で埋めずに拒否します。範囲はDTSA-IIの検出器編集画面に合わせ、チャネル数は2048だけです。
- 一覧の各検出器では、下の例にある`Name`から`Window`までの16項目をすべて書いてください。0が有効な項目も0を明記します。`WindowLayers`・`WindowOpenAreaFraction`・`WindowPreset`は、次の窓の方式に従います。この必須項目の検査は登録用一覧だけに適用し、旧設定・既存pool・モデルの検出器JSONには従来の読込規則を使います。
- 窓（`Window`）は次から選びます。
  - `NoWindow`: 窓なし。
  - `Layers`: 層ごとに組成式（`Formula`）・密度（`DensityGPerCm3`、g/cm³）・厚さ（`ThicknessNm`、nm）を書きます。組成式は鉱物の端成分と同じ「元素記号＋個数」の並びで、括弧は使えません。支持グリッドがある場合だけ、開口率を`WindowOpenAreaFraction`（0より大きく1以下）に書きます。1（グリッドなし）は書かない場合と同じに扱います。
  - `Preset`: DTSA-IIの既製の窓（`XRayWindowFactory.WindowTypes`の名前）を`WindowPreset`に書きます。`Custom Table`と`No window`は使えません。入れたDTSA-IIの版に無い名前は、生成時に理由を付けて失敗します。
- 同じ名前のまま値を書き換えると、名前が同じで値の違うpoolやモデルができます。poolは値で区別されるので混ざりませんが、名前では区別できなくなります。

```json
{
  "Version": 1,
  "Detectors": [
    {
      "Name": "example",
      "ChannelCount": 2048,
      "ChannelWidth": 10,
      "ZeroOffset": 0,
      "ResolutionFwhmAtMnKa": 126,
      "DetectorArea": 30,
      "Elevation": 35,
      "Azimuth": 90,
      "SpecimenToDetectorDistance": 50,
      "OptimalWorkingDistance": 10,
      "SiThickness": 0.45,
      "AluminumLayer": 20,
      "GoldLayer": 6,
      "NickelLayer": 0,
      "DeadLayer": 0.2,
      "Window": "Layers",
      "WindowLayers": [ { "Formula": "C5H8O2", "DensityGPerCm3": 1.15, "ThicknessNm": 50 } ]
    }
  ]
}
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

<!-- 261109Codex: 公開版でも測定時間Bの指定総本数を配分する。 -->
通常のモデル作成画面では、測定時間Bを選んだ状態でも「学習スペクトル数/鉱物」で総本数を指定できます。2000本なら1・5・10・20・50秒を各400本、1000本なら各200本に配分します。5で割り切れない余りは短い測定時間から1本ずつ配分します。総本数は5本以上にしてください。生成は既存の完成済みスペクトルを再利用して不足分を追加し、学習にも指定した総本数を使います。GUIなし生成の `MINERASCOPE_SIM_TARGET` も測定時間Bと併用できます。

<!-- 261021Codex: 選別した通常機能の挙動だけを公開版へ記載する。 -->
モデル作成画面で「分類」または「分類＋OCN」を選択できます。分類＋OCNは分類モデルを学習した後、同じ採用入力から未学習判定器を学習して1つのモデルとして保存します。分類候補と鉱物別確率には分類モデルの出力を使い、未学習検知に保存済みOCN判定器を自動で使います。未学習検知OFFも引き続き利用できます。OCNだけが学習できない場合は理由を通知して分類モデルと作成できた定量モデルを保存します。中止や入力変更では既存モデルを保持します。

新しい通常モデルの未知判定の距離倍率は1.0です。保存済みモデルの閾値は変更しません。カーボン蒸着膜厚は入力値に固定し、通常画面からの新規生成で膜厚のランダム化は行いません。詳細設定の高さはDesignerのパネル寸法と余白に従います。

## 複数起動での生成と学習

<!-- 261021Codex: 読み取り共有と生成排他の運用境界を明記する。 -->
別のMineraScopeでスペクトルを生成している間も、生成完了が管理ファイルに記録されたデータで学習できます。生成中や未完了のデータは学習に使いません。学習準備はpoolの管理状態を書き換えず、生成同士は引き続き排他制御します。同じモデル保存先への同時作成も拒否します。
