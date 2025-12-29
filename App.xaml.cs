using System.Windows;

namespace SimpleViewer;

public partial class App : Application
{
    // XAML の Startup="OnStartup" に対応する正しいシグネチャ
    private void OnStartup(object? sender, StartupEventArgs e)
    {
        var mainWindow = new MainWindow();

        // コマンドライン引数があれば最初のパスを渡す
        if (e.Args != null && e.Args.Length > 0)
        {
            mainWindow.InitialPath = e.Args[0];
        }

        mainWindow.Show();
    }
}