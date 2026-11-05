# Preset B の自動検証

組成・測定時間の計画と保存・再利用を、実際のアプリ内クラスで検証するコンソールプログラムです。
外部のテストフレームワークは使用しません。失敗した場合は終了コード1を返します。

リポジトリ直下で実行します。

```powershell
dotnet build MineraScope.sln -c Debug
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll
```

検証する内容は、組成の選択・反復回数、各秒数の正確な本数、固定seedでの再現性、不等比率のシャッフル、プレビューの読み取り専用性、中断・再開、異なる計画間の再利用、予定表の復元と不整合拒否、学習用データの過不足、使用データの記録です。

<!-- 260907Codex: Preparation changes are checked by read counts and exact selections, not flaky wall-clock assertions. -->
学習準備については、管理ファイルの重複読込削減、変更・削除・再保存時の再読込、互換poolの選択維持、B／従来単一時間の採用ファイルと順序の一致、採用ファイルの破損・欠損拒否、進捗通知と中止も検証します。Bの学習準備は今回の計画に採用するCompletedファイルを検査し、採用しない過去のスペクトルは変更しません。ただし、同条件の互換poolが複数あるときの保存先選択では、従来どおり全候補の実ファイルを検査します。

速度の検証は同じ5 poolを2巡するときのJSON読込回数（60回→5回）で行います。実データで何秒短縮できるかは別途確認が必要です。アプリの学習準備ログには各工程の秒数と読込・再利用回数を表示し、同じ情報を `tf-train-debug.log` に残します。保存済み計画の再計算・照合は、不整合検出と既存計画の互換性を維持するため省略していません。

検証用スペクトルは各テスト専用の一時フォルダーへ作成し、終了時にそのフォルダーだけを削除します。ユーザーのスペクトルやモデルは使用・変更しません。

DTSA-IIの計算、TensorFlowの学習、GUI操作、実データでの全工程の確認は含みません。

## 検出器の窓の層

<!-- 261103Claude: 窓の層の通常テストを公開版にも加える。 -->
通常テストで、窓なしの条件キーと生成スクリプトが窓の追加前に記録した値と一致することを確かめます。あわせて、窓の層の読み書き、DTSA-IIスクリプトへの出力、不正な値の拒否、層の違うpoolを再利用しないことも検証します。スクリプトでは、金・ニッケルだけの窓層が検出器側の金・ニッケル層を置き換えないよう、検出器の層より先に窓を付けることを確かめます。DTSA-IIは起動しません。

## 分類学習率の検証

<!-- 260930Codex: Native checks are optional and use only synthetic data. -->
通常の検証では、新規設定の学習率 `0.0001`、過去の設定JSONの `0.001`、明示した値の保存・読込、不正値の拒否、学習結果への記録を確認します。

分類の実際の重み更新を確認する場合は、ビルド後に次を実行します。同じ初期重みと合成スペクトルで、既定値と明示した `0.0001` の結果が一致し、`0.001` の結果と異なることを検証します。

```powershell
dotnet run --project MineraScope.Tests -c Debug -p:Platform=x64 --no-build -- --classification-learning-rate-smoke
```

## 同名モデルの上書き検証

<!-- 260930Codex: Managed checks detect content changes; the opt-in native check exercises saved weights and map preparation. -->
通常テストでは、サイズと更新日時が同じファイルの内容変更、付随ファイルの追加・削除を検出できることを確認します。

```powershell
dotnet run --project MineraScope.Tests -c Debug -p:Platform=x64 --no-build -- --model-refresh-smoke
```

この追加検証は一時フォルダーの合成モデルだけを使い、分類・定量の同名上書き後の出力変更、未変更モデルの再利用、マップ準備時の更新とブロック処理での再利用を確認します。モデルの内容照合の時間とバイト数も表示します。実測データや保存済みの研究モデルは変更しません。

## 無信号スペクトルの検証

<!-- 261016Codex: 公開版の任意検証は合成スペクトルだけを使用する。 -->
全ゼロ、マスク後に信号が残らない入力、正常入力と欠損ファイルを検証します。学習入力とキャッシュへの混入を防ぎ、失敗時に既存出力を保持することを確認します。実測データや利用者のモデルは変更しません。

```powershell
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --spectrum-signal-smoke
```

## 分類開始条件の検証

<!-- 261016Codex: 分類の件数・クラス数の検査だけを公開版へ反映する。 -->
通常テストで異なる鉱物が2種類以上あること、学習側に完全な1バッチを確保できること、検証側が0件でないことを確認します。直接の学習呼び出しが件数不足を拒否し、既存出力を保持することは次の合成データ検証で確認できます。

```powershell
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --classification-count-smoke
```

## 分類の採用重みと保存の検証

<!-- 261016Codex: ネイティブ一時配列の解放後も分類器が利用可能なことを確認する。 -->
2クラスの合成スペクトルだけで3エポック学習し、最良エポックの選択、前処理・ラベルの保存、分類モデルの再読込と有限な確率出力を確認します。専用の一時フォルダーだけを使用します。

```powershell
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --classification-persistence-smoke
```

## モデル選択の維持

<!-- 261016Codex: STA上の非表示コントロールで選択状態を検証する。 -->
一時的なコンボボックスだけを構築し、優先モデルが消えた場合の選択維持、優先モデルへの切り替え、以前の選択も消えた場合、空一覧を確認します。フォームを表示せず、利用者の設定・モデルを変更しません。

```powershell
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --model-selection-smoke
```

## 分類＋OCNとプロセス間共有

<!-- 261021Codex: 公開版の通常契約と任意の合成実学習を区別する。 -->
通常テストに保存済みOCN判定器の整合性、少数クラスの分割、読み取りと生成の共存、生成同士の排他、JSONの一貫した公開、読み取り専用の学習準備を含みます。テスト専用の子プロセスと一時フォルダーだけを使います。

```powershell
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --product-ocn-smoke
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --training-sample-smoke
```

追加検証は2〜7種類の合成データによる分類＋OCNの学習・保存・再読込、単発・ファイル・マップ用バッチの判定一致、中止と入力変更時の既存モデル保持、件数不足の定量モデルの省略、分類モデルのみの上書き保存を確認します。実測データや利用者のモデルは使いません。

<!-- 261106Codex: 公式版の配置・起動検証を公開版の通常テストへ加える。 -->
## 公式DTSA-IIの起動

通常テストは21区分です。公式版の区分では、偽の配置を使い、自動検出・保存済みパスの優先・旧MSI版の拒否・起動引数を確認します。Polarisの版付きjar名とOberonの版なしjar名を扱い、同梱Javaがなければ拒否します。実際のDTSA-IIは起動しません。

<!-- 261106Codex: 登録一覧と非対話入力の検証を通常テストへ加える。 -->
## 検出器登録とGUIなし生成の入力

「Detector profile list」では、合成した旧設定の移行、自由な名前の照合、重複名・未登録名・未知の項目・基本16項目の欠落の拒否、有効な退避コピーからの復旧、保存失敗時の保持を確認します。必須項目の検査は登録一覧だけに適用し、既存の設定・モデル・manifestの読込とpool再利用を保ちます。窓の区分では組成式、開口率、既製窓、値の範囲、丸めずに渡す軸とスクリプトの失敗理由も確認します。

「Headless simulation overrides」では、MINERASCOPE_SIM_CARBONの0を蒸着なしとして受け付け、負値と非有限値を拒否します。MINERASCOPE_SIM_TARGETは1以上の整数に限り、小数や範囲外の値を拒否します。未指定は保存設定を使い、値はinvariant cultureで読みます。旧ランダム化設定を残したJSONも読めますが、生成の膜厚は画面と同じ入力値に固定します。実際のDTSA-II、利用者の設定・pool・モデルは使いません。

<!-- 261106Codex: 通常の制御テストと任意の実DTSA-II比較を公開版へ加える。 -->
<!-- 261103Codex: 制御可能な実行器による通常テストと、実DTSA-IIの任意比較を区別する。 -->
## シミュレーションの実行順と比較生成

通常テストの「Simulation scheduling and batch checkpoints」では、実Javaを起動せず、ジョブの終了と中止後の回収を合図で制御します。20並列・1000本の20ジョブ×50本、少数の不足、複数測定時間の分割、プレビューとの一致、全体の同時実行上限、バッチをまたぐ投入、バッチの全結果がそろってからの保存、保存の直列化、保存中のジョブ実行、進捗の件数と結果順を確認します。中止・保存失敗時は実行中ジョブの回収を待ち、部分保存をCompleted、未生成分をPendingに残すことも検証します。

実際の速度を比較する場合は、ビルド後に次を明示実行します。GUIなし生成を含め、別の生成が`spectrum-pool-mutation.lock`を持っている間は実行できません。出力先は存在しない専用フォルダーを指定してください。通常テストには含みません。

```powershell
$simulationBenchmarkOutput = Join-Path $env:TEMP ('ms-sim-' + (Get-Date -Format 'yyMMdd-HHmmss'))
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --simulation-benchmark $simulationBenchmarkOutput
```

Olivine・Pyroxeneの2鉱物×100本を新規poolへ生成し、すべての出力を既存のmanifest検証で確認します。検出器、SEM-EDX条件、組成刻み、並列数には保存済みGUI設定を使います。測定時間Bは使わず、保存済みの単一測定時間を使います。生成条件の設定値と既存のpoolは変更しません。検出器一覧がまだ無い場合は、通常の初回起動と同じく旧設定から一覧を自動作成します。Ctrl+Cでは通常の中止処理で停止します。

新規poolでは組成の予約順が乱択されるため、変更後の測定では第3引数に変更前の出力先を指定してください。基準側のmanifestだけを新規poolへ複製して全予約をPendingとし、スペクトルはコピーせずに生成し直します。`benchmark-plan.json`の完全一致をプロセス起動前に検査するので、物理条件やジョブ分割が異なれば生成を始めません。

```powershell
$simulationBenchmarkAfter = Join-Path $env:TEMP ('ms-sim-after-' + (Get-Date -Format 'yyMMdd-HHmmss'))
dotnet MineraScope.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/MineraScope.Tests.dll --simulation-benchmark $simulationBenchmarkAfter $simulationBenchmarkOutput
```

`benchmark-plan.json`のSHA-256でも予約の一致を確認できます。`benchmark-result.json`には完了件数と対応する`simulate-*.log`を記録します。比較にはログの`run end`の全体秒数と`resources end`の`averageCpuPercent`・`processStarts`・`maxActive`を使います。CPU使用率は当該runのJavaのCPU時間を論理CPU数で正規化した値で、PC全体の使用率ではありません。旧ログにCPU時間が無い場合は、同じ計測を入れた変更前の実行ファイルで採り直します。

通常のビルド出力をGUIが使用中の場合は、`dotnet build MineraScope.sln -c Debug -p:OutDir=<専用の絶対パス>`で出力先を分け、そのフォルダーの`MineraScope.Tests.dll`を実行します。変更前後の実行ファイルは別々の出力先に保持し、それぞれ異なる新規poolで順番に測定してください。単回の比較値はばらつきを含むため、速度向上の一般的な保証としては扱いません。

<!-- 261106Codex: プレビューの保護対象と設定一覧の初回移行を区別する。 -->
GUIなし生成のdryrunはpoolの予約・復旧・manifest更新を行いません。起動ログは出力し、検出器一覧が無ければ旧設定から一覧を初回移行します。窓の組成式は末尾のLF・CRLFも拒否し、同じ物理へ改行の有無だけで別の条件キーを作らないことを検証します。
