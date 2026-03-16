# JustTimeShader

VRChat内でプレイヤーのローカルタイム（現在時刻や日付）を表示するためのシェーダーです。標準シェーダーに加え、lilToon対応版を同梱しています。

## VCCで追加

[Add to VCC](vcc://vpm/addRepo?url=https://rimelate.github.io/JustTimeShader/index.json)

## 特徴

* 現在時刻・日付のオーバーレイ表示
* lilToon対応 (`JustTimeShader_liltoon`)
* 専用Inspector GUIによる設定
* 任意のフォントからテクスチャを生成できるフォントベイク機能（エディタ拡張）

## 動作要件

* Unity 2022.3.22f1(VRChat推奨バージョン)
* VRChat SDK
* lilToon (lilToon対応版シェーダーを使用する場合のみ)

## 導入・使用方法

1. 本リポジトリのファイルをUnityプロジェクトの `Assets` フォルダ内に配置します。
2. 新規マテリアルを作成し、Shaderから `JustTimeShader` または `JustTimeShader_liltoon` を選択します。
3. マテリアルのInspectorから、表示形式や色などを設定します。
4. 既存のフォントを使用したい場合は、Unityの上部メニューから Font Baker を起動し、テクスチャを生成・適用してください。
5. オリジナルのフォントを使用したい場合は下記のように16分割したテクスチャを作成してください。（16マス目は空白です。）

0  1  2  3
4  5  6  7
8  9  /  -
:  AM PM [空白]
