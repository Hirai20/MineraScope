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
