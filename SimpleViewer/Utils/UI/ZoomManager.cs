using System;
using System.Windows;
using SimpleViewer.Models;

namespace SimpleViewer.Utils.UI
{
    /// <summary>
    /// ズーム倍率の管理を行うユーティリティクラス。
    /// ビュー側に対して変更通知を行うためのイベントを提供する。
    /// </summary>
    public class ZoomManager
    {
        // ズームのステップ幅（±単位）
        private const double ZoomStep = 0.1;
        // 最大ズーム倍率
        private const double MaxZoom = 10.0;
        // 最小ズーム倍率
        private const double MinZoom = 0.1;

        /// <summary>
        /// 現在のズーム倍率（1.0 = 100%）。外部からの設定は SetZoom 経由で行う。
        /// </summary>
        public double ZoomFactor { get; private set; } = 1.0;

        /// <summary>
        /// 現在のズームモード（手動 / 幅に合わせる / ページに合わせる）
        /// </summary>
        public ZoomMode CurrentMode { get; private set; } = ZoomMode.Manual;

        /// <summary>
        /// UI 側にズーム変更を通知するイベント。
        /// イベントハンドラは変更後の状態を参照して UI を更新する。
        /// </summary>
        public event EventHandler? ZoomChanged;

        /// <summary>
        /// ズーム倍率を更新し、イベントを発火させます。
        /// 引数の倍率は許容範囲にクランプされます。
        /// </summary>
        /// <param name="factor">設定するズーム倍率</param>
        /// <param name="mode">この変更が発生したモード</param>
        public void SetZoom(double factor, ZoomMode mode)
        {
            ZoomFactor = Math.Clamp(factor, MinZoom, MaxZoom);
            CurrentMode = mode;
            // 変更を通知
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// ズームイン（ステップ分拡大）を行う。
        /// モードは手動に設定される。
        /// </summary>
        public void ZoomIn() => SetZoom(ZoomFactor + ZoomStep, ZoomMode.Manual);

        /// <summary>
        /// ズームアウト（ステップ分縮小）を行う。
        /// モードは手動に設定される。
        /// </summary>
        public void ZoomOut() => SetZoom(ZoomFactor - ZoomStep, ZoomMode.Manual);

        /// <summary>
        /// ズームをリセットして 100% に戻す。
        /// </summary>
        public void ResetZoom() => SetZoom(1.0, ZoomMode.Manual);

        /// <summary>
        /// ズームモードを設定し、必要に応じて view/content のサイズから倍率を再計算する。
        /// </summary>
        /// <param name="mode">設定するズームモード</param>
        /// <param name="viewSize">表示領域のサイズ</param>
        /// <param name="contentSize">コンテンツ（ページ等）のサイズ</param>
        public void SetMode(ZoomMode mode, Size viewSize, Size contentSize)
        {
            CurrentMode = mode;
            UpdateZoom(viewSize, contentSize);
        }

        /// <summary>
        /// 現在のモードに基づいてズーム倍率を再計算します。
        /// FitWidth/FitPage の場合は viewSize と contentSize から適切な倍率を算出し、
        /// 計算後は SetZoom を通じて通知を行います。
        /// Manual の場合は何もしません。
        /// </summary>
        /// <param name="viewSize">表示領域のサイズ</param>
        /// <param name="contentSize">コンテンツのサイズ</param>
        public void UpdateZoom(Size viewSize, Size contentSize)
        {
            // サイズが不正な場合は処理しない
            if (contentSize.Width <= 0 || contentSize.Height <= 0 || viewSize.Width <= 0 || viewSize.Height <= 0)
                return;

            double newFactor = ZoomFactor;

            switch (CurrentMode)
            {
                case ZoomMode.FitWidth:
                    // 表示領域の幅に合わせる
                    newFactor = viewSize.Width / contentSize.Width;
                    break;
                case ZoomMode.FitPage:
                    // 幅・高さの比率の小さい方に合わせる（ページ全体が収まるように）
                    newFactor = Math.Min(viewSize.Width / contentSize.Width, viewSize.Height / contentSize.Height);
                    break;
                case ZoomMode.Manual:
                    // 手動モードでは再計算しない
                    return;
            }

            SetZoom(newFactor, CurrentMode);
        }

        /// <summary>
        /// 現在のズーム状態をユーザー向けの文字列（例: "100% (幅)"）で返します。
        /// </summary>
        /// <returns>表示用のズーム文字列</returns>
        public string GetZoomText()
        {
            string suffix = CurrentMode switch
            {
                ZoomMode.FitWidth => " (幅)",
                ZoomMode.FitPage => " (全)",
                _ => ""
            };
            return $"{(ZoomFactor * 100):0}%{suffix}";
        }
    }
}
