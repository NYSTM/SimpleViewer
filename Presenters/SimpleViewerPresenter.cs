using System.IO;
using System.Windows.Media.Imaging;
using SimpleViewer.Models;

namespace SimpleViewer.Presenters;

public class SimpleViewerPresenter(IView view)
{
    private IImageLoader? _loader;
    private int _currentIndex = 0;
    private DisplayMode _displayMode = DisplayMode.Single; // 起動時は単一表示
    private readonly SpreadManager _spreadManager = new();

    public DisplayMode CurrentDisplayMode => _displayMode;

    /// <summary>
    /// ファイルまたはフォルダを開く
    /// </summary>
    public async Task OpenSourceAsync(string path)
    {
        try
        {
            CloseSource();

            string targetPath = path;
            string? targetFileName = null;

            // 1. パスの種類を判定
            if (File.Exists(path))
            {
                string ext = Path.GetExtension(path).ToLower();
                if (ext == ".zip")
                {
                    _loader = new ZipImageLoader();
                }
                else if (ext == ".pdf")
                {
                    _loader = new PdfImageLoader();
                }
                else
                {
                    // 単体画像の場合は親フォルダを対象にする
                    _loader = new LocalImageLoader();
                    targetPath = Path.GetDirectoryName(path) ?? path;
                    targetFileName = Path.GetFileName(path);
                }
            }
            else if (Directory.Exists(path))
            {
                _loader = new LocalImageLoader();
            }

            if (_loader == null) return;

            // 2. ローダーの初期化
            await _loader.InitializeAsync(targetPath);

            // 3. 開始インデックスの特定
            if (targetFileName != null && _loader is LocalImageLoader localLoader)
            {
                _currentIndex = localLoader.GetIndexByFileName(targetFileName);
            }
            else
            {
                _currentIndex = 0;
            }

            // 4. 表示更新（ここでIView.UpdateProgressが呼ばれ、サイドバーが構築される）
            await UpdateDisplayAsync();
        }
        catch (Exception ex)
        {
            view.ShowError($"読み込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 高速なサムネイルを取得する (IImageLoaderに実装が必要)
    /// </summary>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (_loader == null) return null;
        // 各Loaderに実装した「サイズ制限デコード」版を呼び出す
        return await _loader.LoadThumbnailAsync(index, width);
    }

    public void CloseSource()
    {
        _loader?.Dispose();
        _loader = null;
        _currentIndex = 0;
    }

    public async Task NextPageAsync()
    {
        if (_loader == null) return;
        int step = (_displayMode == DisplayMode.Single) ? 1 : 2;
        await MoveToAsync(_currentIndex + step);
    }

    public async Task PreviousPageAsync()
    {
        if (_loader == null) return;
        int step = (_displayMode == DisplayMode.Single) ? 1 : 2;
        await MoveToAsync(_currentIndex - step);
    }

    public async Task JumpToPageAsync(int index)
    {
        await MoveToAsync(index);
    }

    public async Task ToggleDisplayModeAsync()
    {
        _displayMode = _displayMode switch
        {
            DisplayMode.Single => DisplayMode.SpreadRTL,
            DisplayMode.SpreadRTL => DisplayMode.SpreadLTR,
            DisplayMode.SpreadLTR => DisplayMode.Single,
            _ => DisplayMode.Single
        };
        await UpdateDisplayAsync();
    }

    private async Task MoveToAsync(int targetIndex)
    {
        if (_loader == null) return;
        int oldIndex = _currentIndex;
        _currentIndex = Math.Clamp(targetIndex, 0, _loader.TotalPages - 1);

        // インデックスが変わった場合、または初回表示の場合
        if (oldIndex != _currentIndex || targetIndex == 0)
        {
            await UpdateDisplayAsync();
        }
    }

    private async Task UpdateDisplayAsync()
    {
        if (_loader == null)
        {
            view.SetImages(null, null);
            return;
        }

        // 1. 現在のメイン画像をロード
        var currentImg = await _loader.LoadPageAsync(_currentIndex);
        if (currentImg == null) return;

        // 2. 横長判定 (元画像が横長なら強制的に単一表示)
        bool isWideImage = currentImg.PixelWidth > currentImg.PixelHeight;

        if (isWideImage || _displayMode == DisplayMode.Single)
        {
            view.SetImages(currentImg, null);
        }
        else
        {
            // 見開き計算ロジック
            var (leftIdx, rightIdx) = _spreadManager.GetPageIndices(_currentIndex, _loader.TotalPages, _displayMode);

            int otherIdx = (_currentIndex == leftIdx) ? rightIdx : leftIdx;
            BitmapSource? otherImg = (otherIdx != -1) ? await _loader.LoadPageAsync(otherIdx) : null;

            if (_displayMode == DisplayMode.SpreadRTL)
                view.SetImages(otherImg, currentImg); // 右綴じ
            else
                view.SetImages(currentImg, otherImg); // 左綴じ
        }

        // 3. UIのステータスバーやスライダー、サイドバーの更新を通知
        view.UpdateProgress(_currentIndex + 1, _loader.TotalPages);
    }

    public void SetDisplayMode(DisplayMode mode)
    {
        _displayMode = mode;
    }
}