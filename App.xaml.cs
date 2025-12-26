using System.Windows;

namespace SimpleViewer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected void OnStartup(object sender, StartupEventArgs e)
    {
        // MainWindow のインスタンスを手動で作成
        var mainWindow = new MainWindow();

        // コマンドライン引数（e.Args）をチェック
        // エクスプローラーで「プログラムから開く」やファイルを.exeへドロップした際、
        // e.Args[0] にファイルパスが格納されます。
        if (e.Args.Length > 0)
        {
            // MainWindow.xaml.cs で定義した InitialPath プロパティにパスを渡す
            mainWindow.InitialPath = e.Args[0];
        }

        // ウィンドウを表示（StartupUriがないため、ここで明示的にShowを呼ぶ）
        mainWindow.Show();
    }
}