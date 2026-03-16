# JustTimeShader

VRChat内でプレイヤーのローカルタイム（現在時刻や日付）を表示するためのシェーダーです。標準シェーダーに加え、lilToon対応版を同梱しています。

## インストール

以下のURLをVCCに手動追加するか**[Releases](https://github.com/rimelate/JustTimeShader/releases)**から最新版をダウンロードしてください。

https://rimelate.github.io/JustTimeShader/index.json

## 特徴

* 現在時刻・日付のオーバーレイ表示
* lilToon対応 (`JustTimeShader_liltoon`)
* 専用Inspector GUIによる設定
* 任意のフォントからテクスチャを生成できるフォントベイク機能（エディタ拡張）

## 動作要件

* Unity 2022.3.22f1 (VRChat推奨バージョン)
* VRChat SDK
* lilToon (lilToon対応版シェーダーを使用する場合のみ)

## 導入・使用方法

1. VCCで上記リポジトリを追加します。
2. Package Managerから **JustTimeShader** をインストールします。
3. 新規マテリアルを作成し、Shaderから `JustTimeShader` または `JustTimeShader_liltoon` を選択します。
4. マテリアルのInspectorから表示形式や色を設定します。

既存のフォントを使用する場合は、Unity上部メニューから **Font Baker** を起動してテクスチャを生成してください。

オリジナルフォントを使用する場合は、以下のような16分割テクスチャを作成してください。（16マス目は空白）
0 1 2 3
4 5 6 7
8 9 / -
: AM PM [空白]
