namespace SimpleViewer.Utils.Trees
{
    /// <summary>
    /// ツリーモデルのノードを表す軽量クラス。
    /// TreeBuilder から利用することを想定する。以前は TreeBuilder 内部クラスとして定義されていた。
    /// </summary>
    internal class TreeNodeModel
    {
        /// <summary>ノードの表示名</summary>
        public string Name { get; set; }

        /// <summary>
        /// 子ノード。名前で検索できるように自前で保持する（大文字小文字を区別しない）。
        /// </summary>
        public Dictionary<string, TreeNodeModel> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// このノードがページに対応している場合のページインデックス（未設定なら null）。
        /// </summary>
        public int? PageIndex { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="name">ノード名</param>
        public TreeNodeModel(string name) { Name = name; }
    }
}
