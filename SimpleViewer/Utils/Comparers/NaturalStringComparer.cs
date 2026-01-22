using System;

namespace SimpleViewer.Utils.Comparers
{
    /// <summary>
    /// ファイル名などを人間が期待する自然順で比較するための比較器。
    /// Windows のネイティブ API `StrCmpLogicalW` を利用します。
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        // Windows の shlwapi.dll に定義された StrCmpLogicalW を呼び出す
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        /// <summary>
        /// 2 つの文字列を比較して順序を返します。
        /// null 値は空文字列として扱います。
        /// </summary>
        public int Compare(string? x, string? y)
        {
            // null を空文字列に変換して P/Invoke に渡す
            var sx = x ?? string.Empty;
            var sy = y ?? string.Empty;
            try
            {
                return StrCmpLogicalW(sx, sy);
            }
            catch
            {
                // P/Invoke に失敗した場合はフォールバックで OrdinalIgnoreCase を利用
                return string.Compare(sx, sy, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
