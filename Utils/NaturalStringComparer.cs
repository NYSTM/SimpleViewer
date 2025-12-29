namespace SimpleViewer.Utils
{
    /// <summary>
    /// ファイル名を人間が期待する自然順（ナチュラルソート）で比較するための比較器。
    /// 例: "1.jpg", "2.jpg", "10.jpg" の順に並ぶように比較します。
    /// 
    /// Windows 環境ではシェル提供の `StrCmpLogicalW` を利用して自然順比較を行います。
    /// この実装は Windows 専用の P/Invoke を使用しているため、クロスプラットフォームでの
    /// 利用を想定する場合は別実装（.NET の自然順実装やライブラリ）を導入してください。
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        // Windows の shlwapi.dll に定義されている StrCmpLogicalW を呼び出す
        // この関数は文字列内の数値を数値として解釈した自然順比較を行う
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        /// <summary>
        /// 2 つの文字列を比較して順序を返します。
        /// null 値は空文字列として扱います（NullReference を避けるための安全策）。
        /// 戻り値は StrCmpLogicalW の返す値をそのまま利用します（負: x&lt;y、0: 等価、正: x&gt;y）。
        /// </summary>
        public int Compare(string? x, string? y)
        {
            // StrCmpLogicalW は非 null を期待するため、null の場合は空文字にフォールバックする
            return StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
        }
    }
}
