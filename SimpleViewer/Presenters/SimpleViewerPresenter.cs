using SimpleViewer.Models;
using SimpleViewer.Models.ImageSources;
using SimpleViewer.Models.Imaging.Decoders;
using SimpleViewer.Presenters.Navigation;
using SimpleViewer.Services;
using SimpleViewer.Utils.Caching;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Presenters;

/// <summary>
/// アプリケーションの主要な Presenter クラス。
/// View (IView) と Model (IImageSource 等) の橋渡しを行い、ページ遷移、キャッシュ管理、プリフェッチ等を担当します。
/// UI 更新は IView 経由で行われるため、Presenter は非 UI スレッドで画像読み込みを行い、
/// 最終的な UI 更新は View に委譲します。
/// </summary>
public class SimpleViewerPresenter
{
    /// <summary>
    /// View（UI 操作用）のインスタンス。Presenter は UI 更新をこのインターフェース経由で行う。
    /// </summary>
    private readonly IView view;

    // デコーダ（IImageDecoder）を注入してデコード責務を分離
    private readonly IImageDecoder _decoder;

    // 現在開いている画像ソース
    private IImageSource? currentSource;

    /// <summary>
    /// ページナビゲーション管理
    /// </summary>
    private readonly PageNavigationManager _navigationManager;

    /// <summary>
    /// 表示モード（単一表示 / 見開き表示）
    /// </summary>
    public DisplayMode CurrentDisplayMode => _navigationManager.CurrentDisplayMode;

    // 画像キャッシュ管理を専用インターフェイス経由で扱う
    private readonly IImageCache imageCache;

    // サムネイル取得を担当するサービス
    private readonly ThumbnailService thumbnailService;

    // 非同期制御
    private CancellationTokenSource? prefetchCts;
    private readonly SemaphoreSlim loadLock = new(1, 1);

    /// <summary>
    /// コンストラクタ: Presenter を初期化します。
    /// IImageCache を注入可能にしてテストや差し替えを容易にします。
    /// IImageDecoder を注入してデコーダを差し替え可能にします。
    /// </summary>
    /// <param name="view">UI 更新を受け持つ View 実装（IView）</param>
    /// <param name="imageCache">画像キャッシュの実装（省略時は ImageCacheManager を利用）</param>
    /// <param name="decoder">画像デコーダ（省略時は SkiaImageDecoder を利用）</param>
    public SimpleViewerPresenter(IView view, IImageCache? imageCache = null, IImageDecoder? decoder = null)
    {
        this.view = view;
        this.imageCache = imageCache ?? new ImageCacheManager();
        _decoder = decoder ?? new SkiaImageDecoder();
        thumbnailService = new ThumbnailService(_decoder);
        _navigationManager = new PageNavigationManager();
    }

    /// <summary>
    /// 指定パスのソースを開き、ページ数を取得して最初のページを表示します。
    /// 既に開かれているソースがあれば CloseSource() で閉じます。
    /// </summary>
    /// <param name="path">開く対象のファイルまたはフォルダのパス</param>
    public async Task OpenSourceAsync(string path)
    {
        CloseSource();
        try
        {
            // ソースの生成（ファクトリに委譲）
            currentSource = await ImageSourceFactory.CreateSourceAsync(path, _decoder);
            int totalCount = await currentSource.GetPageCountAsync();
            _navigationManager.SetTotalPageCount(totalCount);

            if (totalCount > 0) await JumpToPageAsync(0);
        }
        catch (Exception ex)
        {
            // View にエラーを表示させる
            view.ShowError($"ソースを開けません: {ex.Message}");
        }
    }

    /// <summary>
    /// 現在のソースを閉じ、関連するタスクやキャッシュをクリアします。
    /// 非同期のディスクキャッシュ削除はバックグラウンドで実行されます。
    /// </summary>
    public void CloseSource()
    {
        // 進行中の操作をキャンセル
        _navigationManager.CancelNavigation();
        prefetchCts?.Cancel();

        // キャッシュの解放
        imageCache.Clear();
        thumbnailService.ClearAllCache();

        // ディスクキャッシュのクリアはバックグラウンドで実行
        _ = Task.Run(async () =>
        {
            try { await thumbnailService.ClearAllCacheAsync().ConfigureAwait(false); } catch { }
        });

        // 現在のソースを破棄
        currentSource?.Dispose();
        currentSource = null;
        _navigationManager.Reset();

        // メモリ確保のため軽く GC を促す（頻繁に呼ばないこと）
        GC.Collect();
    }

    /// <summary>
    /// 指定インデックスへジャンプし、必要な画像を読み込んで View を更新します。
    /// 表示モードに応じて左右の画像を決定します。
    /// </summary>
    /// <param name="index">ジャンプ先ページインデックス（0 始まり）</param>
    public async Task JumpToPageAsync(int index)
    {
        if (currentSource == null || _navigationManager.TotalPageCount == 0) return;

        // ナビゲーション管理に移動を委譲
        bool isForward = _navigationManager.JumpToPage(index);
        var token = _navigationManager.GetNewNavigationToken();

        try
        {
            // 表示すべきページインデックスを計算
            var (leftIndex, rightIndex) = _navigationManager.CalculatePageIndices();

            BitmapSource? left = null;
            BitmapSource? right = null;

            // 画像の読み込み
            if (leftIndex.HasValue)
                left = await GetOrLoadImageAsync(leftIndex.Value, token);

            if (rightIndex.HasValue)
                right = await GetOrLoadImageAsync(rightIndex.Value, token);

            if (!token.IsCancellationRequested)
            {
                // View に描画を委譲
                view.SetImages(left, right);
                view.UpdateProgress(_navigationManager.CurrentPageIndex + 1, _navigationManager.TotalPageCount);

                // 必要に応じてプリフェッチを開始
                StartSmartPrefetching(isForward);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// キャッシュを参照し、存在すれば返す。存在しなければソースからロードしてキャッシュに格納します。
    /// 同時アクセスは SemaphoreSlim で制御します。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="token">キャンセルトークン</param>
    /// <returns>読み込んだ <see cref="BitmapSource"/> または null</returns>
    private async Task<BitmapSource?> GetOrLoadImageAsync(int index, CancellationToken token)
    {
        if (index < 0 || index >= _navigationManager.TotalPageCount || currentSource == null) return null;
        if (imageCache.TryGet(index, out var cached)) return cached;

        await loadLock.WaitAsync(token);
        try
        {
            // ロック取得後に再チェック
            if (imageCache.TryGet(index, out var cachedAgain)) return cachedAgain;

            // メモリ状況に応じて不要キャッシュを削除
            imageCache.PurgeIfNeeded(_navigationManager.CurrentPageIndex);

            // 実際のページ画像取得は IImageSource に委譲
            var bitmap = await currentSource.GetPageImageAsync(index);
            if (bitmap != null)
            {
                if (bitmap.CanFreeze) bitmap.Freeze();
                imageCache.Add(index, bitmap);
                return bitmap;
            }
        }
        finally
        {
            loadLock.Release();
        }
        return null;
    }

    /// <summary>
    /// スマートなプリフェッチを行う。進行方向を優先しつつ近傍ページを順次読み込む。
    /// バックグラウンドで実行し、キャンセル可能です。
    /// </summary>
    /// <param name="isForward">進行方向（true=前方）</param>
    private void StartSmartPrefetching(bool isForward)
    {
        prefetchCts?.Cancel();
        prefetchCts = new CancellationTokenSource();
        var token = prefetchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                // ナビゲーション管理からプリフェッチ対象を取得
                var queue = _navigationManager.GetPrefetchIndices(isForward);

                foreach (int idx in queue)
                {
                    if (token.IsCancellationRequested) return;
                    if (!imageCache.TryGet(idx, out _))
                    {
                        await GetOrLoadImageAsync(idx, token);
                        await Task.Delay(50, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    /// <summary>
    /// 次ページへ移動します。表示モードにより移動量は変わります。
    /// </summary>
    public async Task NextPageAsync()
    {
        int targetIndex = _navigationManager.MoveToNextPage();
        await JumpToPageAsync(targetIndex);
    }

    /// <summary>
    /// 前のページへ移動します。表示モードにより移動量は変わります。
    /// </summary>
    public async Task PreviousPageAsync()
    {
        int targetIndex = _navigationManager.MoveToPreviousPage();
        await JumpToPageAsync(targetIndex);
    }

    /// <summary>
    /// 表示モードを切り替え、キャッシュをクリアして現在ページを再表示します。
    /// </summary>
    public async Task ToggleDisplayModeAsync()
    {
        _navigationManager.ToggleDisplayMode();
        imageCache.Clear();
        thumbnailService.ClearAllCache();
        await JumpToPageAsync(_navigationManager.CurrentPageIndex);
    }

    /// <summary>
    /// 表示モードを明示的に設定します。
    /// </summary>
    /// <param name="mode">設定する表示モード</param>
    public void SetDisplayMode(DisplayMode mode)
    {
        _navigationManager.SetDisplayMode(mode);
    }

    /// <summary>
    /// 指定インデックスのサムネイルを取得します。ThumbnailService を利用します。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">要求幅（ピクセル）</param>
    /// <param name="token">キャンセル用トークン（オプション）</param>
    /// <returns>サムネイル画像または null</returns>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width, CancellationToken? token = null)
    {
        if (currentSource == null) return null;
        return await thumbnailService.GetThumbnailAsync(currentSource, index, width, token ?? CancellationToken.None);
    }

    /// <summary>
    /// 総ページ数を返します（UI コンポーネント向けユーティリティ）。
    /// </summary>
    /// <returns>総ページ数</returns>
    public int GetTotalPageCount() => _navigationManager.TotalPageCount;

    /// <summary>
    /// ツリー表示用のファイル一覧を取得して返します。
    /// ソースがない場合は空のリストを返します。
    /// </summary>
    public async Task<IReadOnlyList<string>> GetFileListAsync()
    {
        if (currentSource == null) return Array.Empty<string>();
        try
        {
            return await currentSource.GetFileListAsync();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 現在表示中のページを再読み込みします。
    /// キャッシュをクリアしてから現在のページを再表示することで、
    /// 設定変更（EXIF Orientationなど）を即座に反映させます。
    /// </summary>
    public async Task ReloadCurrentPageAsync()
    {
        if (currentSource == null || _navigationManager.TotalPageCount == 0) return;

        // 画像キャッシュとサムネイルキャッシュを完全にクリア（ディスクキャッシュも含む）
        imageCache.Clear();
        thumbnailService.ClearAllCache();

        // ディスクキャッシュのクリアを非同期で実行
        _ = Task.Run(async () =>
        {
            try { await thumbnailService.ClearAllCacheAsync().ConfigureAwait(false); } catch { }
        });

        // 現在のページを再読み込み
        await JumpToPageAsync(_navigationManager.CurrentPageIndex);
    }
}