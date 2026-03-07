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
/// View (IView) と Model (IImageSource 等) の橋渡しを行います。
/// </summary>
/// <remarks>
/// <para>主な責務:</para>
/// <list type="bullet">
/// <item><description>画像ソースの管理（開く、閉じる）</description></item>
/// <item><description>ページナビゲーション（次へ、前へ、ジャンプ）</description></item>
/// <item><description>画像キャッシュの管理</description></item>
/// <item><description>スマートプリフェッチによるパフォーマンス最適化</description></item>
/// <item><description>表示モードの切り替え（単一表示、見開き表示）</description></item>
/// </list>
/// <para>
/// UI 更新は IView 経由で行われるため、Presenter は非 UI スレッドで画像読み込みを行い、
/// 最終的な UI 更新は View に委譲します。
/// </para>
/// </remarks>
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
    
    // ソースのロード/クローズを排他制御するロック
    private readonly SemaphoreSlim _sourceOperationLock = new(1, 1);

    // ソースのロード状態を管理
    private volatile bool _isLoadingSource;
    
    // ページナビゲーション中フラグ（過剰な並行実行を防ぐ）
    private volatile bool _isNavigating;

    /// <summary>
    /// 現在ソースをロード中かどうかを示します。
    /// </summary>
    public bool IsLoadingSource => _isLoadingSource;

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
    /// 新しいソースを開きます。
    /// ソースを切り替える前に、前のソースのキャッシュを確実にクリアします。
    /// 排他制御により、同時に複数のソースを開くことを防ぎます。
    /// </summary>
    /// <param name="path">開く対象のファイルまたはフォルダのパス</param>
    public async Task OpenSourceAsync(string path)
    {
        // 既にロード中の場合は処理をスキップ（早期リターン）
        if (_isLoadingSource) return;

        // ソース操作のロックを取得（排他制御）
        await _sourceOperationLock.WaitAsync();
        
        IImageSource? newSource = null;
        int totalCount = 0;
        
        try
        {
            // ロック取得後に再度チェック
            if (_isLoadingSource) return;

            _isLoadingSource = true;

            // 進行中のナビゲーションが完了するまで待機（短時間のスピンウェイト）
            var spinWait = new SpinWait();
            while (_isNavigating)
            {
                spinWait.SpinOnce();
                if (spinWait.Count > 1000) // 約100ms
                {
                    // ナビゲーションをキャンセルして強制的に進む
                    _navigationManager.CancelNavigation();
                    await Task.Delay(10);  // キャンセルが反映されるまで待機
                    break;
                }
            }

            // 前のソースを確実にクリア（キャッシュクリアを待機）
            await CloseSourceInternalAsync();
            
            // ソースの生成（ファクトリに委譲）
            newSource = await ImageSourceFactory.CreateSourceAsync(path, _decoder);
            totalCount = await newSource.GetPageCountAsync();
            
            // ソースを確定
            currentSource = newSource;
            _navigationManager.SetTotalPageCount(totalCount);

            // ここまでが危険な期間。ソースが確定したらロックとフラグを解放
            _isLoadingSource = false;
        }
        catch (Exception ex)
        {
            _isLoadingSource = false;  // エラー時も確実に解除
            newSource?.Dispose();  // 作成したソースを破棄
            
            // View にエラーを表示させる
            view.ShowError($"ソースを開けません: {ex.Message}");
            return;  // エラー時は最初のページ表示をスキップ
        }
        finally
        {
            _sourceOperationLock.Release();  // ← ロックを早期解放
        }

        // ロック外で最初のページを表示（デッドロック回避）
        if (totalCount > 0)
        {
            await JumpToPageAsync(0);
        }
    }

    /// <summary>
    /// 現在のソースを閉じ、関連するタスクやキャッシュをクリアします。
    /// キャッシュクリアを確実に完了させてから戻ります。
    /// 外部から呼び出す場合は、排他制御を行います。
    /// </summary>
    public async Task CloseSourceAsync()
    {
        await _sourceOperationLock.WaitAsync();
        try
        {
            await CloseSourceInternalAsync();
        }
        finally
        {
            _sourceOperationLock.Release();
        }
    }

    /// <summary>
    /// 現在のソースを閉じる内部実装。
    /// ロックは呼び出し側で取得済みの前提です。
    /// </summary>
    private async Task CloseSourceInternalAsync()
    {
        // 進行中の操作をキャンセル
        _navigationManager.CancelNavigation();
        prefetchCts?.Cancel();

        // キャッシュの解放
        imageCache.Clear();

        // ディスクキャッシュのクリアを確実に待機
        try
        {
            await thumbnailService.ClearAllCacheAsync().ConfigureAwait(false);
        }
        catch
        {
            // キャッシュクリアの失敗は致命的ではないため無視
        }

        // 現在のソースを破棄
        currentSource?.Dispose();
        currentSource = null;
        _navigationManager.Reset();
    }

    /// <summary>
    /// 指定インデックスへジャンプし、必要な画像を読み込んで View を更新します。
    /// 表示モードに応じて左右の画像を決定します。
    /// 過剰な並行実行を防ぐため、既にナビゲーション中の場合は最新の要求で置き換えます。
    /// </summary>
    /// <param name="index">ジャンプ先ページインデックス（0 始まり）</param>
    public async Task JumpToPageAsync(int index)
    {
        if (currentSource == null || _navigationManager.TotalPageCount == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] 早期リターン: currentSource={currentSource != null}, TotalPageCount={_navigationManager.TotalPageCount}");
            return;
        }

        // 既にナビゲーション中の場合は、進行中の処理をキャンセル
        if (_isNavigating)
        {
            System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] ナビゲーション中をキャンセル: index={index}");
            _navigationManager.CancelNavigation();
        }

        _isNavigating = true;
        System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] 開始: index={index}");
        try
        {
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
                    System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] 画像表示: left={left != null}, right={right != null}");
                    // View に描画を委譲
                    view.SetImages(left, right);
                    view.UpdateProgress(_navigationManager.CurrentPageIndex + 1, _navigationManager.TotalPageCount);

                    // 必要に応じてプリフェッチを開始
                    StartSmartPrefetching(isForward);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] キャンセルされました: index={index}");
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] OperationCanceledException: index={index}");
            }
        }
        finally
        {
            _isNavigating = false;
            System.Diagnostics.Debug.WriteLine($"[JumpToPageAsync] 完了: index={index}");
        }
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

        // 現在のソースを保存（フォルダ切り替え検出用）
        var sourceAtStart = currentSource;

        await loadLock.WaitAsync(token);
        try
        {
            // ロック取得後に再チェック
            if (imageCache.TryGet(index, out var cachedAgain)) return cachedAgain;

            // ソースが変わっていたら中止（フォルダ切り替え後の古いタスク）
            if (currentSource != sourceAtStart) return null;
            
            // ソースロード中は処理を中止
            if (_isLoadingSource) return null;

            // メモリ状況に応じて不要キャッシュを削除
            imageCache.PurgeIfNeeded(_navigationManager.CurrentPageIndex);

            // 実際のページ画像取得は IImageSource に委譲
            var bitmap = await currentSource.GetPageImageAsync(index);
            if (bitmap != null)
            {
                // キャッシュに追加する前にソースを再確認（三重チェック）
                if (currentSource == sourceAtStart && !_isLoadingSource)
                {
                    if (bitmap.CanFreeze) bitmap.Freeze();
                    imageCache.Add(index, bitmap);
                    return bitmap;
                }
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
        await thumbnailService.ClearAllCacheAsync().ConfigureAwait(false);
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
    /// フォルダ切り替え時の混入を防ぐため、ソース参照を保存してチェックします。
    /// </summary>
    /// <param name="index">ページインデックス</param>
    /// <param name="width">要求幅（ピクセル）</param>
    /// <param name="token">キャンセル用トークン（オプション）</param>
    /// <returns>サムネイル画像または null</returns>
    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width, CancellationToken? token = null)
    {
        // ソースロード中はサムネイル取得をスキップ
        if (_isLoadingSource) return null;
        
        // 現在のソースを保存（フォルダ切り替え検出用）
        var sourceAtStart = currentSource;
        if (sourceAtStart == null) return null;
        
        var result = await thumbnailService.GetThumbnailAsync(sourceAtStart, index, width, token ?? CancellationToken.None);
        
        // 取得後にソースが変わっていたら、またはロード中になっていたら結果を破棄
        if (currentSource != sourceAtStart || _isLoadingSource) return null;
        
        return result;
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