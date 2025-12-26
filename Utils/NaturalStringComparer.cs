namespace SimpleViewer.Utils
{
    /// <summary>
    /// ファイル名を人間が期待する順序（1.jpg -> 2.jpg -> 10.jpg）で並べるための比較器
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);

        public int Compare(string? x, string? y)
        {
            return StrCmpLogicalW(x ?? "", y ?? "");
        }
    }
}
