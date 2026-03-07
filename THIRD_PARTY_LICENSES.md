# Third-Party Licenses

このソリューションには、プロジェクトごとに外部ライブラリが含まれます。

## SimpleViewer

`SimpleViewer` で利用している第三者ライブラリのライセンス全文は、次のファイルを参照してください。

- `SimpleViewer/THIRD_PARTY_LICENSES.txt`
- `SimpleViewer/licenses/`
- `SimpleViewer/licenses/GLFW_LICENSE.txt`

補足:

- `SimpleViewer` の publish 出力には、推移依存として `glfw3.dll` が含まれる場合があります。
- この DLL は `OpenTK.redist.glfw` 経由で配布される `GLFW` 由来のネイティブライブラリです。
- `GLFW` のライセンスは一般に `zlib/libpng License` として案内されます。

## その他のプロジェクト

テストプロジェクトおよびベンチマークプロジェクトで利用している NuGet パッケージは、それぞれのパッケージライセンスに従います。
ライセンス確認が必要な場合は、各 `.csproj` に記載された依存パッケージと NuGet 上のライセンス情報を参照してください。
