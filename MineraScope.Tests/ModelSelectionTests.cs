using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace MineraScope.Tests
{
    // 261016Codex: ユーザー設定や表示画面を使わず、一覧更新後の選択をSTA上で検証する。
    internal static class ModelSelectionTests
    {
        public static void Run()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var combo = new ComboBox();
                    ModelComboBinder.Populate(combo, ["First", "Second"], "Second");
                    TestAssert.Equal("Second", combo.SelectedItem as string, "存在する優先モデルを選択する。");
                    ModelComboBinder.Populate(combo, ["First", "Second"], "missing");
                    TestAssert.Equal("Second", combo.SelectedItem as string, "優先名がなくても前の選択を保持する。");
                    ModelComboBinder.Populate(combo, ["First", "Second"], "First");
                    TestAssert.Equal("First", combo.SelectedItem as string, "存在する優先名には切り替えられる。");
                    ModelComboBinder.Populate(combo, ["Second"], "missing");
                    TestAssert.Equal("Second", combo.SelectedItem as string, "前の選択も消えた場合は先頭を選ぶ。");
                    ModelComboBinder.Populate(combo, [], "missing");
                    TestAssert.Equal(-1, combo.SelectedIndex, "空一覧では選択を残さない。");
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
