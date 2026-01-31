using SimpleViewer.Models;
using SimpleViewer.Models.Configuration;
using SimpleViewer.Utils.Configuration;
using SimpleViewer.Utils.UI;
using System.Windows;
using System.Windows.Controls;

namespace SimpleViewer.Presenters.Controllers;

/// <summary>
/// 設定の読み込み・保存と、読み込んだ設定を UI に適用する責務を持つコントローラー。
/// <para>
/// - ファイル入出力自体は SettingsManager に委譲する。
/// - UI への適用（ウィンドウサイズ、サイドバー表示など）は呼び出し側が UI スレッドであることを想定している。
/// - 非同期保存 API を提供することで UI スレッドをブロックしない保存が可能。
/// </para>
/// </summary>
public class SettingsController
{
    private readonly SettingsManager _settingsManager;
    private readonly SimpleViewerPresenter _presenter;
    private readonly ZoomManager _zoomManager;
    private readonly ColumnDefinition _sidebarColumn;
    private readonly Window _window;

    /// <summary>
    /// SettingsController を初期化します。
    /// </summary>
    /// <param name="settingsBaseDirectory">設定ファイルを保存する基底ディレクトリ（通常は実行フォルダなど）</param>
    /// <param name="presenter">Presenter（表示モード設定に使用）</param>
    /// <param name="zoomManager">Zoom 管理オブジェクト（ズーム設定の復元に使用）</param>
    /// <param name="sidebarColumn">サイドバー列定義（表示/非表示の復元に使用）</param>
    /// <param name="window">対象ウィンドウ（サイズ/状態の復元に使用）</param>
    public SettingsController(string settingsBaseDirectory, SimpleViewerPresenter presenter, ZoomManager zoomManager, ColumnDefinition sidebarColumn, Window window)
    {
        _settingsManager = new SettingsManager(settingsBaseDirectory ?? throw new ArgumentNullException(nameof(settingsBaseDirectory)));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _zoomManager = zoomManager ?? throw new ArgumentNullException(nameof(zoomManager));
        _sidebarColumn = sidebarColumn ?? throw new ArgumentNullException(nameof(sidebarColumn));
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// 設定を読み込み、関連オブジェクトや UI に適用します。
    /// <para>
    /// - 呼び出しは基本的に UI スレッドで行ってください（ウィンドウサイズや GridLength の操作が行われるため）。
    /// - 設定ファイルが存在しない場合は既定値が適用されます。
    /// </para>
    /// </summary>
    public void LoadAndApply()
    {
        var s = _settingsManager.LoadSettings();

        // 表示モードの復元
        _presenter.SetDisplayMode(s.DisplayMode);

        // ズーム設定の復元: 保存時は文字列で保存しているため Enum.TryParse で復元
        ZoomMode restoredMode = ZoomMode.Manual;
        if (Enum.TryParse(s.ZoomMode, out ZoomMode zMode)) restoredMode = zMode;
        _zoomManager.SetZoom(s.ZoomFactor, restoredMode);

        // サイドバー表示状態の復元
        _sidebarColumn.Width = s.IsSidebarVisible ? new GridLength(200) : new GridLength(0);

        // ウィンドウサイズと状態を復元。WindowState は最大化などを表現する列挙値。
        // 注意: 実際の表示スケーリングやモニタ構成によって収まらなくなる場合があるため
        // 必要に応じて範囲チェックや補正を追加してください。
        _window.Width = s.WindowWidth;
        _window.Height = s.WindowHeight;
        if (s.WindowState == (int)WindowState.Maximized) _window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// 現在の UI 状態から設定を作成して同期的に保存します。
    /// <para>ファイル I/O による待機が発生するため、UI スレッドでの長時間ブロックに注意してください。</para>
    /// </summary>
    public void SaveFromCurrentState()
    {
        var s = CreateSettingsFromCurrentState();
        _settingsManager.SaveSettings(s);
    }

    /// <summary>
    /// 現在の UI 状態から設定を作成して非同期で保存します。
    /// <para>内部でバックグラウンドスレッドに処理をオフロードするため、UI スレッドをブロックしません。</para>
    /// </summary>
    /// <returns>保存タスク。必要なら呼び出し側で await できます。</returns>
    public Task SaveFromCurrentStateAsync()
    {
        var s = CreateSettingsFromCurrentState();
        return _settingsManager.SaveSettingsAsync(s);
    }

    /// <summary>
    /// 現在の UI/Presenter の状態から AppSettings インスタンスを作成します。
    /// </summary>
    private AppSettings CreateSettingsFromCurrentState()
    {
        // ウィンドウの実際のサイズは ActualWidth/ActualHeight を使って取得
        return new AppSettings
        {
            DisplayMode = _presenter.CurrentDisplayMode,
            ZoomMode = _zoomManager.CurrentMode.ToString(),
            ZoomFactor = _zoomManager.ZoomFactor,
            IsSidebarVisible = _sidebarColumn.Width.Value > 0,
            WindowState = (int)_window.WindowState,
            WindowWidth = _window.ActualWidth,
            WindowHeight = _window.ActualHeight
        };
    }
}
