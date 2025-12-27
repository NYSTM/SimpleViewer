using System;
using System.Windows;

namespace SimpleViewer.Utils
{
    internal enum ZoomMode
    {
        Manual,
        FitWidth,
        FitPage
    }

    internal class ZoomManager
    {
        private const double ZoomStep = 0.1;
        private const double MaxZoom = 10.0;
        private const double MinZoom = 0.1;

        public double ZoomFactor { get; private set; } = 1.0;
        public ZoomMode CurrentMode { get; private set; } = ZoomMode.Manual;

        // UI側に変更を通知するためのイベント
        public event EventHandler? ZoomChanged;

        /// <summary>
        /// ズーム倍率を更新し、イベントを発火させます。
        /// </summary>
        public void SetZoom(double factor, ZoomMode mode)
        {
            ZoomFactor = Math.Clamp(factor, MinZoom, MaxZoom);
            CurrentMode = mode;
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ZoomIn() => SetZoom(ZoomFactor + ZoomStep, ZoomMode.Manual);
        public void ZoomOut() => SetZoom(ZoomFactor - ZoomStep, ZoomMode.Manual);
        public void ResetZoom() => SetZoom(1.0, ZoomMode.Manual);

        public void SetMode(ZoomMode mode, Size viewSize, Size contentSize)
        {
            CurrentMode = mode;
            UpdateZoom(viewSize, contentSize);
        }

        /// <summary>
        /// 現在のモードに基づいてズーム倍率を再計算します。
        /// </summary>
        public void UpdateZoom(Size viewSize, Size contentSize)
        {
            if (contentSize.Width <= 0 || contentSize.Height <= 0 || viewSize.Width <= 0 || viewSize.Height <= 0)
                return;

            double newFactor = ZoomFactor;

            switch (CurrentMode)
            {
                case ZoomMode.FitWidth:
                    newFactor = viewSize.Width / contentSize.Width;
                    break;
                case ZoomMode.FitPage:
                    newFactor = Math.Min(viewSize.Width / contentSize.Width, viewSize.Height / contentSize.Height);
                    break;
                case ZoomMode.Manual:
                    return; // 手動時は何もしない
            }

            SetZoom(newFactor, CurrentMode);
        }

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