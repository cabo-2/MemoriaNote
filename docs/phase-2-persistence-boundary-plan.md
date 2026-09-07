# Phase 2: 永続化境界の実施計画

- 作成日: 2026-09-07
- 基準: `main` の CORE-015 完了時点
- 対象: `CORE-020` から `CORE-025`
- 関連文書: [設計改善・リファクタリング計画](design-refactoring-roadmap.md)

## 1. 目的

Phase 2 の目的は、SQLite と EF Core の具体的な処理を `Note`、`Metadata`、`Workgroup` から分離し、永続化を明示的な境界として扱えるようにすることである。

単に `DbContext` を別クラスで包むことが目的ではない。次の状態を目標にする。

- DB 接続の生成方法と寿命が一か所で管理される。
- 検索、Page CRUD、Metadata、migration の transaction 境界がユースケース単位で分かる。
- ドメインオブジェクトのプロパティ参照、`ToString`、equality が暗黙に DB へアクセスしない。
- SQLite 固有の SQL、FTS5、trigger、migration をインフラ実装へ閉じ込める。
- 上位層へ `DbContext`、`DbSet`、`IQueryable` を公開しない。
- 既存の SQLite ファイルと backup/restore 形式を維持する。

Phase 2 ではプロジェクト分割を先行させず、まず既存の `core` プロジェクト内に境界を作る。物理的な assembly 分割は、依存方向が安定してから別 PR で検討する。

## 2. 現在の永続化仕様

### 2.1 保存単位と DB 接続

1つのノートは1つの SQLite ファイルに対応する。現在は `Note.DataSource` と検索結果の `Content.OwnerDataSource` に、そのファイルパスを保持している。

`NoteDbContext` は文字列のパスを直接受け取り、`OnConfiguring` で SQLite connection string を組み立てる。パスがない場合は `:memory:` を使用する。また、logger factory も `NoteDbContext` 自身が生成している。

現在、`new NoteDbContext(...)` は次の領域に分散している。

| 領域 | 主な利用目的 |
| --- | --- |
| `Note` | DB作成、migration、Page CRUD、検索、件数取得 |
| `Workgroup` | ノート横断検索の件数取得 |
| `Metadata` / `DataSourceTracker` | metadataの遅延読込とプロパティ単位保存 |
| `NoteUtil` | import/export、backup/restore |
| CLI | 追加対象ファイルがSQLiteとして開けるかの確認 |
| tests | 実SQLiteの状態確認 |

この構造では、呼び出し元が同一 transaction を共有できず、テスト時に接続設定を差し替えることも難しい。

### 2.2 テーブルと索引

現在の migration は、概ね次の構造を作る。

| オブジェクト | 役割 | 主キー・対応関係 |
| --- | --- | --- |
| `Pages` | Page本文を含む正本 | `Rowid`が主キー、`Uuid`がalternate key |
| `Contents` | 本文を除いたPage要約read model | `Uuid`が主キー、`Rowid`と`Uuid`で`Pages`に対応 |
| `Metadata` | ノート属性のkey-value保存 | `Key`が主キー |
| `FtsIndex` | `Pages`を外部コンテンツとするFTS5索引 | `content_rowid='Rowid'`で`Pages.Rowid`に対応 |

`Pages` と `Contents` は、ともに `(Name, Index)` の非unique indexを持つ。CORE-014では、Page作成・更新・名称変更・削除時のIndex補正とtransactionを整備したが、DBのunique制約追加はmigration互換性の検討が必要なため延期されている。

### 2.3 `Pages`、`Contents`、`FtsIndex` の同期

`Pages` が書き込みの正本である。`Pages_Insert`、`Pages_Update`、`Pages_Delete` triggerが、`Contents` と `FtsIndex` を同期する。

```text
Page CRUD
   |
   v
 Pages  ---------------------> 本文・属性の正本
   |  insert/update/delete trigger
   +-------------------------> Contents（一覧・見出し検索用の要約）
   +-------------------------> FtsIndex（本文・見出しの全文索引）
```

`Contents` は偶然重複しているテーブルではなく、本文を読まずに一覧、件数、空検索、部分一致検索を処理するread modelである。直接更新せず、`Pages` のtrigger経由でのみ更新することを設計上の規則とする。

`FtsIndex` はFTS5のexternal-content tableである。external-content indexはアプリケーション側で正本との同期を保証する必要があり、triggerは作成前の既存行や破損を自動修復しない。SQLiteは整合性検査と正本からの再構築コマンドを提供しているため、CORE-025ではそれを利用する。[SQLite FTS5公式文書](https://www.sqlite.org/fts5.html#external_content_table_pitfalls)

### 2.4 Page CRUD

Page CRUDは現在 `Note` と `PageClient` に分かれている。

- 作成: 同名グループの末尾へ追加し、既存の欠番も含めてIndexを1から連番へ正規化する。
- 本文・属性更新: 呼び出し元が変更したIndexを採用せず、元のIndexを維持する。
- 名称変更: 移動元を詰め、移動先グループの末尾へ追加する。
- 削除: 対象がなければ何もせず、存在する場合は同名グループのIndexを詰める。
- 作成・更新・削除とIndex補正は、それぞれ1 transactionで処理する。
- `Pages` の変更により、triggerが `Contents` と `FtsIndex` を同期する。

この振る舞いは [PageCrudCharacteristicsTests.cs](../tests/MemoriaNote.Core.Tests/Functional/PageCrudCharacteristicsTests.cs) で固定されている。

### 2.5 検索

検索は `Task<SearchResult>` と `CancellationToken` に統一済みである。現在は `Note` がSQLite SQLを組み立て、`Workgroup` がノートごとの件数と結果を集約する。

- 見出し完全一致と本文検索では `FtsIndex` を利用する。
- 空検索と見出し部分一致では主に `Contents` を利用する。
- 見出し検索では `*` / `?` をglob風ワイルドカードとして扱うが、本文検索では現在FTS tokenのprefix展開としては扱わない。
- 並び順は `Name COLLATE NOCASE ASC, Index ASC` である。
- `skipCount` / `takeCount` は並び順適用後のsliceで、`SearchResult.Count` はslice前の総件数である。
- ワークグループ検索はノート順を保って全体のsliceを作る。
- 結果には更新先解決用の所有ノートパスが含まれる。
- cancellationはEF Coreのasync APIへ渡される。

現在のSQLは文字列連結と `FromSqlRaw` を使用する箇所がある。SQL parameter化とFTS5検索式の構文制御は別問題であり、CORE-021では両方を扱う必要がある。

### 2.6 Metadata

`Metadata` は見かけ上は通常のオブジェクトだが、各getterが未読込値をDBから読み、各setterがその場で `SaveChanges` を実行する。`ToString`、`Clone`、`GetHashCode`、equalityも `DataSourceTracker.Create` を経由してDBを再読込する。

保存項目は `Name`、`Title`、`Version`、`Description`、`Author`、`ReadOnly`、`Tag`、`CreateTime` である。複数項目の編集は複数transactionになるため、途中失敗時に一部だけ保存される可能性がある。

`CreateTime` は現在 `yyyyMMddhhmmss` 形式で保存される一方、読込時に通常の `DateTime.Parse` を使うため、既存の特性テストでは `FormatException` が確認されている。また12時間表記にAM/PM情報がないため、既存値から午後を完全には復元できない。この互換性問題はCORE-023で明示的に扱う。

### 2.7 ノート作成とmigration

`Note.Create` と `Note.Migrate` が、ファイル存在確認、`DbContext`生成、EF migration、初期metadata保存を直接行う。

- `Create`: 出力ファイルが存在すると失敗し、migration後に `Name`、`Title`、`Version` を個別保存する。
- `Migrate`: 入力ファイルが存在しないと失敗し、migration後に `Version` を保存する。
- migration履歴は既存DBとbackup/restoreの互換性に直結する。

### 2.8 現在のテスト保護

Phase 0・1で、実SQLiteを使用する次の回帰テストが用意されている。

- migrationとPage lifecycle
- Page CRUD、Index補正、transaction rollback
- metadataの現行保存形式と既知の`CreateTime`読込不具合
- 見出し・本文・空・完全一致・ワイルドカード・ページング検索
- ワークグループ横断検索、所有ノート、キャンセル、latest-wins
- backup/restoreとimport/export
- infrastructure error発生時のサービス状態維持

Phase 2の各PRでは、これらのテストを移植または拡張しながら、外部から見える振る舞いを維持する。

## 3. 目標とする永続化境界

### 3.1 推奨ポート

型名は実装時に調整できるが、責務は次の単位で分ける。

| ポート・実装 | 責務 |
| --- | --- |
| `INoteDatabaseFactory` | ノートのlocatorから、設定済みで独立した`NoteDbContext`を生成する |
| `SqliteNoteDatabaseFactory` | connection、EF options、SQLite固有設定を構成する |
| `INoteSearchRepository` | 見出し・本文検索と総件数取得を行う |
| `INoteRepository` | Pageの取得、作成、更新、名称変更、削除をユースケース単位で行う |
| `INoteMetadataRepository` | metadata snapshotの一括読込と一括更新を行う |
| `INoteMigrator` | 新規DB作成、既存DB migration、初期metadata保存を行う |
| `INoteReadModelMaintenance` | `Contents` / `FtsIndex` の検査と明示的再構築を行う |

### 3.2 共通規則

- 現段階のnote locatorは正規化済みデータソースパスとする。永続的な独立`NoteId`の導入は別変更とする。
- `DbContext`は1ユースケース内だけで使用し、Repositoryから返さない。
- transactionを必要とする処理では、同一`DbContext`と同一transactionを最後まで共有する。
- Repository APIはドメインモデル、専用request、専用resultを返し、EF entityのtracking状態へ依存させない。
- `IQueryable`、`DbSet`、raw SQLをRepositoryの外へ公開しない。
- I/Oを行う新規APIは原則 `Task` と `CancellationToken` を使用する。
- 既存の同期公開APIが必要な間は薄い互換facadeとして残し、永続化実装を重複させない。
- SQLite固有例外はCORE-015の分類方針に従い、想定外例外と混同しない。
- Phase 2ではCLIの表示、終了コード、エラー通知方法を変更しない。

## 4. 作業項目

### CORE-020: options/factory経由で`NoteDbContext`を生成

#### 目的

接続文字列、EF options、logger設定、context生成を呼び出し元から分離し、後続Repositoryが同じ生成規則を利用できるようにする。

#### 作業

- `NoteDbContext`に `DbContextOptions<NoteDbContext>` を受け取る構築経路を追加する。
- `INoteDatabaseFactory` とSQLite実装を追加する。
- パスの正規化、connection string生成、context lifetimeの責任位置を決める。
- production codeの直接生成箇所を一覧化し、後続IDの移行先をコメントやissueではなく本文書の対応表で管理する。
- 新規コードでは `new NoteDbContext(path)` を増やさない。
- EF migrationと既存SQLiteファイルをfactory経由で開けることを確認する。

#### 完了条件

- 異なる2つのノートに対して、factoryが混線しないcontextを生成できる。
- contextの所有者と破棄責任が明確で、すべての利用箇所で確実に破棄される。
- 一時SQLite fixtureがfactory経由でも利用できる。
- 既存のmigrationと全回帰テストが通る。
- DB schema、依存package、target frameworkを変更しない。

### CORE-021: 検索SQLを`SqliteNoteSearchRepository`へ集約

#### 目的

SQLite FTS5と検索SQLを `Note` / `Workgroup` から外し、検索条件、件数、並び順、parameter化を一つの実装で管理する。

#### 作業

- `INoteSearchRepository` と `SqliteNoteSearchRepository` を追加する。
- `Note`内の見出し検索・本文検索と、`Workgroup`内のノート別件数SQLを移す。
- SQL値をEF parameterまたは `FromSqlInterpolated` で渡す。
- table名やcolumn名は固定SQLとし、利用者入力をidentifierへ展開しない。
- FTS5 query syntax用の変換処理をSQL escapingから分離する。
- 件数クエリと結果クエリが同じ検索条件を使うようにする。
- cancellation、総件数、並び順、owner情報の契約を維持する。

#### 維持する既存仕様

- heading/full-text、空入力、完全一致、`*`、`?`、`%`、`_`、引用符、Unicodeの結果。
- `Name COLLATE NOCASE ASC, Index ASC` の順序。
- paging後もslice前の総件数を返すこと。
- ワークグループ検索のノート順とlatest-wins。

検索構文そのものを改善する場合は、既存特性テストを先に期待仕様へ更新し、PR本文に互換性変更を記載する。

#### 完了条件

- `Note`と`Workgroup`にraw SQL組み立てが残らない。
- 検索値がSQL文字列連結されない。
- Repositoryから `IQueryable` が漏れない。
- 既存検索テストと、引用符を含む入力・キャンセル・DB障害の追加テストが通る。

### CORE-022: Page CRUDをユースケース指向Repositoryへ移動

#### 目的

Page操作とIndex不変条件を1つの永続化境界へまとめ、1操作のtransaction範囲を明確にする。

#### 作業

- `INoteRepository`に、実際のユースケースに対応する取得・作成・更新・削除APIを定義する。
- `Note`のPage CRUDと `NormalizePageIndexes` をRepository実装へ移す。
- `PageClient` / `ContentClient` の利用をRepository内部へ限定し、不要になれば段階的に廃止する。
- CRUDのすべてでUUIDと所有ノートlocatorを使用し、他ノートへ誤更新しないようにする。
- 作成、名称変更、削除、Index補正をそれぞれ単一transactionで実行する。
- triggerによる `Contents` / `FtsIndex` 更新を維持する。

#### 維持する既存仕様

- 同名PageのIndexは1始まりの連番。
- 同名Pageの通常更新ではIndexを維持。
- 名称変更では移動先末尾へ追加し、移動元を詰める。
- caller指定のIndexで並び順を勝手に変更しない。
- 存在しないPageの削除はno-op。
- transaction失敗時はPageとIndexをすべてrollbackする。

#### 完了条件

- `Note`が `DbContext`、transaction、`PageClient`を直接扱わない。
- CRUD contract testをRepository interfaceに対して実行できる。
- SQLite実装では既存のPage CRUD統合テストが通る。
- `(Name, Index)` unique制約は、既存DBの重複修復とmigration計画なしに追加しない。

### CORE-023: MetadataをI/Oなしのsnapshotへ変更

#### 目的

metadataの参照と永続化を分離し、複数項目を一度に安全に更新できるようにする。

#### 作業

- DB接続を持たない `NoteMetadata` snapshotを定義する。
- `INoteMetadataRepository`に一括loadと一括updateを定義する。
- updateは1つのcontext、1つのtransaction、1回の `SaveChangesAsync` で完了させる。
- `Metadata`のgetter/setter、`ToString`、`Clone`、equality、hash、validationからDBアクセスを除く。
- `NoteKeyValue`の読み書きをRepository内部へ移す。
- 未保存変更と保存済みsnapshotを混同しないAPIにする。
- backup/restoreが扱うmetadataキーと値を維持する。

#### `CreateTime`互換性

- 既存の14桁文字列は破棄・自動書換えしない。
- 現在の値を読んだだけで `FormatException` にならないよう、明示的なformatで解析する。
- 12時間表記で失われたAM/PMは推測修復しない。
- 新規保存形式を24時間表記へ変更する場合は、旧値読込、backup/restore、比較・tag用途をテストし、PRにmigration noteを記載する。

#### 完了条件

- snapshot生成後の全プロパティ参照がDBへアクセスしない。
- `ToString`、clone、equality、validationをDBファイル削除後にも実行できる。
- 複数項目更新が途中失敗時に全rollbackされる。
- metadataの欠落キー、bool、日時のparse失敗が分類可能な結果になる。
- metadata特性テストを新しい正しい仕様へ更新する。

### CORE-024: 作成とmigrationを`INoteMigrator`へ移動

#### 目的

DB lifecycleを `Note` からSQLiteインフラへ移し、新規作成と既存DB更新の責任を一か所にまとめる。

#### 作業

- `INoteMigrator`とSQLite実装を追加する。
- 新規作成、既存DB migration、初期metadata保存を明示的な操作に分ける。
- `Note.Create` / `Note.Migrate` は必要な間だけ互換facadeとして残し、同じ実装へ委譲する。
- 新規作成失敗時の部分ファイルをどう扱うかをテストで固定する。
- migration失敗時に元DBを破損させず、例外をinfrastructure errorとして伝える。
- `NoteUtil.Restore`から利用できる契約を維持する。

#### 完了条件

- `Note`が `Database.Migrate` と初期metadata書込みを直接行わない。
- 新規作成、再migration、存在しないDB、既存出力先、migration失敗を実SQLiteで試験する。
- migration IDと既存DBの読み込み互換性を維持する。
- schema変更が必要な場合は専用migrationとmigration noteを含める。

### CORE-025: read modelとFTS triggerの整合性・再構築

#### 目的

`Contents` と `FtsIndex` を設計上のread modelとして明文化し、正本 `Pages` との不一致を検出・修復できるようにする。

#### 検査内容

- `Pages` / `Contents` の件数一致。
- `Rowid` / `Uuid` の欠落、余分な行、重複、対応不一致。
- `Name`、`Index`、`Tags`、`ContentType`、`CreateTime`、`UpdateTime`、`IsErased` の要約値一致。
- `Pages_Insert` / `Pages_Update` / `Pages_Delete` triggerの存在。
- FTS5内部整合性と、external-content tableである `Pages` との一致。

FTS5とexternal contentの比較には、公式仕様に従ってrankを1にした `integrity-check` を利用する。再構築にはFTS5の `rebuild` コマンドを利用する。

#### 再構築規則

- `Pages`だけを正本とし、`Contents`や`FtsIndex`から `Pages` を変更しない。
- `Contents`はtransaction内で `Pages` から全件再生成する。
- `FtsIndex`はFTS5のrebuild機能で `Pages` から再生成する。
- 修復後に同じ整合性検査を再実行する。
- 初期段階では起動時に黙って自動修復せず、明示的なmaintenance APIとして提供する。
- 失敗時はinfrastructure errorを記録し、可能な範囲でtransactionをrollbackする。

#### 完了条件

- `IntegrityReport`相当の結果から、不一致箇所と件数を判別できる。
- テストで `Contents` の欠落・余分・値不一致を作り、検出できる。
- テストでFTS索引を不整合にし、integrity-checkで検出できる。
- 再構築後に整合性検査、見出し検索、本文検索がすべて成功する。
- 通常のinsert/update/delete後は修復なしで整合性が維持される。

## 5. 推奨実施順序とPR境界

| 順序 | PR | 理由 |
| --- | --- | --- |
| 1 | CORE-020 | 後続実装が共有するcontext生成方法を先に固定する |
| 2 | CORE-021 | raw SQLと横断件数取得を先に隔離し、検索契約を維持する |
| 3 | CORE-022 | transactionを含むPage CRUDを移動する |
| 4 | CORE-023 | 暗黙I/Oを除き、metadata更新をatomicにする |
| 5 | CORE-024 | factoryとmetadata repositoryを使ってDB lifecycleを分離する |
| 6 | CORE-025 | Repository確立後にread modelの検査・修復を追加する |

CORE-021、CORE-022、CORE-023、CORE-024は表上はいずれもCORE-020だけに依存する。ただし `Note.Create` が初期metadataを書き込むため、実装上はCORE-023をCORE-024より先に行うと重複する暫定コードを減らせる。

各PRは1つのroadmap IDだけを扱い、framework/package更新、CLI再設計、プロジェクト分割を混ぜない。

## 6. Production codeの直接`DbContext`生成の移行先

| 現在の場所 | 移行先 |
| --- | --- |
| `Note`の検索 | CORE-021 `INoteSearchRepository` |
| `Workgroup`の件数SQL | CORE-021 `INoteSearchRepository` |
| `Note`のPage CRUD・件数 | CORE-022 `INoteRepository` |
| `Metadata` / `DataSourceTracker` | CORE-023 `INoteMetadataRepository` |
| `Note.Create` / `Note.Migrate` | CORE-024 `INoteMigrator` |
| `Contents` / `FtsIndex`保守 | CORE-025 `INoteReadModelMaintenance` |
| `NoteUtil`のtransfer処理 | factoryを使用する暫定対応後、CORE-041で専用サービスへ移動 |
| CLIのSQLiteファイル確認 | factoryまたは専用validation portへ委譲し、UI動作変更は別Phaseで扱う |

Phase 2完了後、production codeでの直接 `new NoteDbContext(...)` はSQLite factory、EF toolingに必要なfactory、明示的なcomposition rootに限定する。テストがDB内容を直接検査するためのcontext生成は許容する。

## 7. Phase 2全体の完了条件

- `Note` / `Metadata` / `Workgroup`が `NoteDbContext` を直接生成しない。
- `Note` / `Metadata`のプロパティ参照がDB I/Oを発生させない。
- 検索SQLはSQLite検索Repositoryだけに存在し、値がparameter化される。
- Page CRUDとmetadata更新のtransaction境界がユースケース単位で明示される。
- `Pages`を正本、`Contents`と`FtsIndex`をread modelとして検査・再構築できる。
- Repository interfaceにSQLiteを使わない単体テスト、SQLite実装に実DB統合テストを用意する。
- 既存SQLite DB、migration、backup/restoreの互換性が維持される。
- CORE-010〜015で固定した所有権、検索、キャンセル、Index、例外処理の契約が維持される。
- `dotnet build MemoriaNote.sln`、`dotnet test MemoriaNote.sln --no-build`、cspellが成功する。

## 8. Phase 2の対象外

- Domain/Application/Infrastructureの別assembly化。
- ReactiveUIと表示状態をcoreから外す作業。
- CLIのhandler分割、表示、終了コード、エラー通知の変更。
- import/export、backup/restore全体のユースケース化。
- loggerやclockのDI設計全体。
- .NET 10または依存packageへの更新。
- 性能根拠のない複数SQLite DBへの無制限並列アクセス。

これらは永続化境界が安定した後、後続Phaseまたは専用PRで扱う。
