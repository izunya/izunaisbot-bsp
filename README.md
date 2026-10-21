# izunaisbot-bsp — Discord → BeatSaberPlus Chat Bridge

> [English](#english) · [한국어](#한국어) · [日本語](#日本語)

A BSIPA plugin that forwards **Discord channel messages into the in-game BeatSaberPlus chat overlay** (and `!bsr` song requests). When someone types in your Discord channel, it appears live on the Beat Saber BSP Chat overlay; `!bsr <code>` is queued into ChatRequest automatically.

```
[Discord channel] ──(izunaisbot bot)──▶ [WebSocket] ──▶ izunaisbot-bsp ──▶ BeatSaberPlus Chat / ChatRequest
```

> **⚠️ Requires the izunaisbot Discord bot** — this plugin only bridges into the game. You need the **izunaisbot** Discord bot, set up at **<https://izunaisbot.izunya.dev>**.
>
> **⚠️ izunaisbot 디스코드 봇이 필요합니다** — 이 플러그인은 게임으로 연결만 합니다. **izunaisbot** 디스코드 봇이 있어야 하며 **<https://izunaisbot.izunya.dev>** 에서 설정합니다.
>
> **⚠️ izunaisbot Discord ボットが必要です** — このプラグインはゲームへの橋渡しのみ行います。**izunaisbot** ボットが必要で、**<https://izunaisbot.izunya.dev>** で設定します。

---

## English

### What it does
- Shows Discord channel messages on the **BeatSaberPlus_Chat** overlay (with emotes)
- Queues songs into **BeatSaberPlus_ChatRequest** when a message is `!bsr <code>`
- **Discord voice-channel member count** (a Discord-colored ● + count) that **docks right above BeatSaberPlus's viewer-count panel** and follows the chat window when you move it — falls back to the chat panel's bottom-right when the viewer count is hidden, or a standalone panel if Chat isn't running
- **`!bsr` request feedback** — sends the request result (queued / rejected / queue closed) back to the bot so Discord users get a reply
- **Other commands relayed too** — results of `!oops`/`!queue`/`!link`/`!np`/`!skip`/`!who`/`!wip` etc. (BeatSaberPlus ChatRequest & wipbot) are forwarded back to Discord
- **Responses shown in-game** — `!bsr` and the commands above don't just reply on Discord; their results are also drawn on the in-game BSP Chat overlay so the player sees them live
- Auto-reconnects if the connection drops
- **Local web UI** (English/Korean toggle) to configure everything live — no restart
- **Update notice** — shows a banner in the web UI when a newer release is available on GitHub
- **In-game button** in the left Mods tab to open the web UI
- Optionally **opens the web UI in your browser every time Beat Saber launches**

### Requirements
| Need | Notes |
|---|---|
| **Beat Saber 1.44.0 / 1.40.8 / 1.29.1** | Per-version zips are distributed |
| **BSIPA 4.3.0+** | Mod loader |
| **BeatSaberPlus** | `Chat` (required); `ChatRequest` for song requests |
| **izunaisbot Discord bot** | **Required** — the Discord bot this bridge talks to. Set up at <https://izunaisbot.izunya.dev> |
| **izunaisbot bot token** | Issue a `bsp_xxx` token from the [dashboard](https://izunaisbot.izunya.dev/dashboard/me/bsp-bridge) |

### Install
1. Download the latest `izunaisbot-bsp-x.y.z.zip` from [**Releases**](../../releases).
2. Extract it into your **Beat Saber install folder root** → `izunaisbot-bsp.dll` lands in `Plugins\`.
3. Launch the game once, then quit (creates the config file).

### Configure
There are three ways; the web UI is recommended.

**1. Local web UI (recommended)** — open **http://localhost:9001/** in your browser.
Enter your `Token`, toggle auto-reconnect, manage channels (forward on/off), watch the live message log. The bridge URL is preset (`wss://bsp.izunya.dev/bsp`), so you only need the token. Saving applies instantly (no restart). Top-right button cycles English/Korean/Japanese.

**2. In-game** — in the main menu's left **Mods** tab, click the **izunaisbot** button to open the web UI.

**3. JSON file** — edit `UserData\izunaisbot-bsp.json` while the game is closed:
```json
{
    "Url": "wss://bsp.izunya.dev/bsp",
    "Token": "bsp_xxxxxxxxxxxxxxxxxxxxx",
    "AutoReconnect": true,
    "ReconnectIntervalSec": 10,
    "WebUIEnabled": true,
    "WebUIPort": 9001,
    "OpenWebOnLaunch": true,
    "ForwardOnlyCommands": false,
    "DisabledChannels": []
}
```
- **Token** — your `bsp_xxx` token (sent via a cookie header). This is the only thing you normally set.
- **Url** — bridge endpoint, preset to `wss://bsp.izunya.dev/bsp`; advanced/optional
- **WebUIPort** — local web UI port (changing it needs a game restart)
- **OpenWebOnLaunch** — auto-open the web UI in your browser on launch
- **ForwardOnlyCommands** — only forward `!`-commands to the game
- **DisabledChannels** — channel IDs muted from forwarding

### Usage
1. Get a token on the [dashboard](https://izunaisbot.izunya.dev/dashboard/me/bsp-bridge) and pick the Discord channel(s) to forward.
2. Enter your `Token` (web UI or JSON). The URL is already preset.
3. Messages in the Discord channel show up on the BSP Chat overlay.
4. `!bsr <map code>` is added to the song-request queue.

Connected correctly when the game log (`Logs\_latest.log`) shows:
```
[izunaisbot-bsp] Connected: wss://bsp.izunya.dev/bsp
[izunaisbot-bsp] Local web UI: http://localhost:9001/
```

### Troubleshooting
| Symptom | Fix |
|---|---|
| `Discord: URL/Token not set` | Enter Url/Token, then reconnect |
| `disconnected (1008)` | Token invalid/revoked → issue a new one |
| `disconnected (1006)` | Wrong URL or the bot server is down |
| Web UI won't open | Check `WebUIEnabled`, the port, and that the game is running |
| In-game button missing | BSML/Zenject timing — non-fatal; use the web UI instead |
| No chat in game | Make sure the BeatSaberPlus **Chat** module is enabled |
| `!bsr` ignored | The **ChatRequest** module must also be enabled |

### Build it yourself
Needs Windows + .NET SDK 8 + .NET Framework 4.7.2 + Beat Saber (with BSIPA, BeatSaberPlus, BSML). Set `BeatSaberDir` in `Directory.Build.props` (or the env var) to your install.
```cmd
dotnet build -c Release
:: build + copy into the game's Plugins folder
dotnet build -c Release /p:CopyToPlugins=true
```
Pushing a `v*` tag (e.g. `v0.1.1`) triggers GitHub Actions to build, zip, and upload a Release (CI build needs the `BS_REFS_B64` secret containing the game reference DLLs).

---

## 한국어

### 무슨 일을 하나요
- 디스코드 채널 메시지를 **BeatSaberPlus_Chat** 오버레이에 표시 (이모지 포함)
- 메시지가 `!bsr <코드>` 면 **BeatSaberPlus_ChatRequest** 큐에 자동 추가
- **디스코드 음성채널 인원수**(디스코드 색 ● + 숫자)를 **BeatSaberPlus 시청자 수 패널 바로 위**에 붙여 표시하고 채팅창을 옮기면 같이 따라감 — 시청자 수 표시가 꺼져 있으면 채팅 패널 오른쪽 아래로, Chat 모듈이 없으면 독립 패널로 폴백
- **`!bsr` 신청 결과 피드백** — 신청 결과(큐 추가 / 거절 / 큐 닫힘)를 봇으로 다시 보내 디스코드 사용자가 응답을 받음
- **다른 명령도 전달** — `!oops`/`!queue`/`!link`/`!np`/`!skip`/`!who`/`!wip` 등(BeatSaberPlus ChatRequest·wipbot)의 결과도 디스코드로 다시 전달
- **응답을 게임 안에도 표시** — `!bsr` 및 위 명령들의 결과를 디스코드뿐 아니라 게임 안 BSP Chat 오버레이에도 띄워 플레이어가 바로 확인
- 연결이 끊기면 자동 재접속
- **로컬 웹 UI**(영어/한국어 전환)로 모든 설정을 재시작 없이 실시간 변경
- **업데이트 알림** — GitHub에 새 릴리스가 있으면 웹 UI에 배너로 안내
- 메인 메뉴 좌측 **Mods 탭의 인게임 버튼**으로 웹 UI 열기
- 옵션으로 **비트세이버 실행 때마다 웹 UI 자동 열기**

### 작동에 필요한 것
| 필요 | 비고 |
|---|---|
| **Beat Saber 1.44.0 / 1.40.8 / 1.29.1** | 버전별 zip 배포 |
| **BSIPA 4.3.0+** | 모드 로더 |
| **BeatSaberPlus** | `Chat`(필수), 곡 신청은 `ChatRequest` |
| **izunaisbot 디스코드 봇** | **필수** — 이 브리지가 연결하는 디스코드 봇. <https://izunaisbot.izunya.dev> 에서 설정 |
| **izunaisbot 봇 토큰** | [대시보드](https://izunaisbot.izunya.dev/dashboard/me/bsp-bridge)에서 `bsp_xxx` 발급 |

### 설치
1. [**Releases**](../../releases)에서 최신 `izunaisbot-bsp-x.y.z.zip` 다운로드.
2. **Beat Saber 설치 폴더 루트**에 압축 해제 → `izunaisbot-bsp.dll` 이 `Plugins\` 로 들어감.
3. 게임을 한 번 실행했다 종료 (설정 파일 생성).

### 설정
세 가지 방법이 있으며 웹 UI를 권장합니다.

**1. 로컬 웹 UI (권장)** — 브라우저에서 **http://localhost:9001/** 접속.
`Token` 입력, 자동 재접속 토글, 채널 전달 on/off, 실시간 메시지 로그 확인. 브리지 URL은 `wss://bsp.izunya.dev/bsp` 로 고정이라 토큰만 넣으면 됩니다. 저장 시 재시작 없이 즉시 적용. 우측 상단 버튼으로 영어/한국어/일본어 전환.

**2. 인게임** — 메인 메뉴 좌측 **Mods 탭**의 **izunaisbot** 버튼 클릭 → 설정 패널의 **Web Open** 버튼으로 웹 UI 열림.

**3. JSON 파일** — 게임 종료 상태에서 `UserData\izunaisbot-bsp.json` 편집:
```json
{
    "Url": "wss://bsp.izunya.dev/bsp",
    "Token": "bsp_xxxxxxxxxxxxxxxxxxxxx",
    "AutoReconnect": true,
    "ReconnectIntervalSec": 10,
    "WebUIEnabled": true,
    "WebUIPort": 9001,
    "OpenWebOnLaunch": true,
    "ForwardOnlyCommands": false,
    "DisabledChannels": []
}
```
- **Token** — `bsp_xxx` 토큰 (쿠키 헤더로 전송). 보통 이것만 설정하면 됩니다.
- **Url** — 브리지 주소, `wss://bsp.izunya.dev/bsp` 로 고정 (고급/선택)
- **WebUIPort** — 로컬 웹 UI 포트 (변경 시 게임 재시작 필요)
- **OpenWebOnLaunch** — 실행 시 웹 UI 자동 열기
- **ForwardOnlyCommands** — `!` 명령어만 게임으로 전달
- **DisabledChannels** — 전달 음소거할 채널 ID 목록

### 사용 방법
1. [대시보드](https://izunaisbot.izunya.dev/dashboard/me/bsp-bridge)에서 토큰 발급 + 전달할 디스코드 채널 선택.
2. `Token` 입력 (웹 UI 또는 JSON). URL은 이미 고정돼 있습니다.
3. 디스코드 채널 메시지가 BSP Chat 오버레이에 표시됩니다.
4. `!bsr <맵코드>` 는 곡 신청 큐에 자동 추가됩니다.

게임 로그(`Logs\_latest.log`)에 아래가 보이면 정상:
```
[izunaisbot-bsp] Connected: wss://bsp.izunya.dev/bsp
[izunaisbot-bsp] Local web UI: http://localhost:9001/
```

### 문제 해결
| 증상 | 해결 |
|---|---|
| `Discord: URL/Token 미설정` | Url/Token 입력 후 재접속 |
| `disconnected (1008)` | 토큰 무효/회수됨 → 새 토큰 발급 |
| `disconnected (1006)` | URL 오류 또는 봇 서버 꺼짐 |
| 웹 UI 안 열림 | `WebUIEnabled`/포트 확인, 게임 실행 중인지 확인 |
| 인게임 버튼 없음 | BSML/Zenject 타이밍 문제 — 치명적 아님, 웹 UI 사용 |
| 게임에 채팅 안 보임 | BeatSaberPlus **Chat** 모듈 켜짐 확인 |
| `!bsr` 안 먹힘 | **ChatRequest** 모듈도 켜져 있어야 함 |

### 직접 빌드
Windows + .NET SDK 8 + .NET Framework 4.7.2 + Beat Saber(BSIPA·BeatSaberPlus·BSML 포함) 필요. `Directory.Build.props` 의 `BeatSaberDir`(또는 환경변수)를 설치 경로로 설정.
```cmd
dotnet build -c Release
:: 빌드 후 게임 Plugins 폴더로 복사
dotnet build -c Release /p:CopyToPlugins=true
```
`v*` 태그(예: `v0.1.1`)를 push 하면 GitHub Actions 가 빌드 → zip → Release 업로드까지 자동 처리합니다 (CI 빌드에는 게임 참조 DLL 을 담은 `BS_REFS_B64` 시크릿 필요).

---

## 日本語

### 機能
- Discord チャンネルのメッセージを **BeatSaberPlus_Chat** オーバーレイに表示（絵文字対応）
- メッセージが `!bsr <コード>` の場合 **BeatSaberPlus_ChatRequest** のキューに自動追加
- **Discord ボイスチャンネルの人数**（Discord カラーの ● + 数字）を **BeatSaberPlus の視聴者数パネルのすぐ上**に表示し、チャット欄を動かすと一緒に追従 — 視聴者数表示がオフのときはチャットパネルの右下、Chat モジュールが無いときは独立パネルにフォールバック
- **`!bsr` リクエスト結果のフィードバック** — リクエスト結果（キュー追加 / 拒否 / キュー閉鎖）をボットへ返信し、Discord ユーザーに応答が届く
- **他のコマンドも転送** — `!oops`/`!queue`/`!link`/`!np`/`!skip`/`!who`/`!wip` など（BeatSaberPlus ChatRequest・wipbot）の結果も Discord へ返信
- **応答をゲーム内にも表示** — `!bsr` および上記コマンドの結果を Discord だけでなくゲーム内 BSP Chat オーバーレイにも表示し、プレイヤーがその場で確認
- 切断時は自動再接続
- **ローカル Web UI**（英語/韓国語/日本語 切替）で再起動なしに設定をリアルタイム変更
- **アップデート通知** — GitHub に新しいリリースがあると Web UI にバナーで通知
- メインメニュー左の **Mods タブのボタン**から Web UI を開く
- 任意で **Beat Saber 起動時に Web UI を自動で開く**

### 必要なもの
| 必要 | 備考 |
|---|---|
| **Beat Saber 1.44.0 / 1.40.8 / 1.29.1** | 各バージョン用 zip を配布 |
| **BSIPA 4.3.0+** | Mod ローダー |
| **BeatSaberPlus** | `Chat`（必須）、曲リクエストは `ChatRequest` |
| **izunaisbot Discord ボット** | **必須** — このブリッジが通信する Discord ボット。<https://izunaisbot.izunya.dev> で設定 |
| **izunaisbot ボットトークン** | [ダッシュボード](https://izunaisbot.izunya.dev/dashboard/me/bsp-bridge)で `bsp_xxx` を発行 |

### インストール
1. [**Releases**](../../releases) から自分のゲームバージョンに合う `izunaisbot-bsp-x.y.z-bsX.Y.Z.zip` をダウンロード。
2. **Beat Saber インストールフォルダのルート**に展開 → `Plugins\izunaisbot-bsp.dll` に入ります。
3. ゲームを一度起動して終了（設定ファイルが生成されます）。

### 設定
3 つの方法があり、Web UI を推奨します。

**1. ローカル Web UI（推奨）** — ブラウザで **http://localhost:9001/** を開く。
`Token` を入力、自動再接続の切替、チャンネル転送 on/off、リアルタイムのメッセージログ確認。ブリッジ URL は `wss://bsp.izunya.dev/bsp` に固定なのでトークンだけでOK。保存すると再起動なしで即適用。右上のボタンで英語/韓国語/日本語を切替。

**2. ゲーム内** — メインメニュー左の **Mods タブ**の **izunaisbot** ボタン → 設定パネルの **Web Open** ボタンで Web UI を開く。

**3. JSON ファイル** — ゲーム終了状態で `UserData\izunaisbot-bsp.json` を編集（キーは上の JSON 例と同じ）。
- **Token** — `bsp_xxx` トークン（Cookie ヘッダーで送信）。通常はこれだけ設定。
- **Url** — ブリッジアドレス、`wss://bsp.izunya.dev/bsp` 固定（上級/任意）
- **WebUIPort** — ローカル Web UI のポート（変更時はゲーム再起動が必要）
- **OpenWebOnLaunch** — 起動時に Web UI を自動で開く
- **ForwardOnlyCommands** — `!` コマンドのみゲームへ転送
- **DisabledChannels** — 転送をミュートするチャンネル ID 一覧

### 使い方
1. [ダッシュボード](https://izunaisbot.izunya.dev/dashboard/me/bsp-bridge)でトークンを発行し、転送する Discord チャンネルを選択。
2. `Token` を入力（Web UI または JSON）。URL は設定済み。
3. Discord チャンネルのメッセージが BSP Chat オーバーレイに表示されます。
4. `!bsr <マップコード>` は曲リクエストのキューに自動追加されます。

### トラブルシューティング
| 症状 | 対処 |
|---|---|
| `Discord: URL/Token not set` | Token を入力して再接続 |
| `disconnected (1008)` | トークンが無効/失効 → 新しく発行 |
| `disconnected (1006)` | URL が誤り、またはボットサーバーが停止 |
| Web UI が開かない | `WebUIEnabled`/ポート、ゲーム起動中か確認 |
| ゲーム内ボタンが無い | BSML/Zenject のタイミング — 致命的ではない、Web UI を使用 |
| ゲームにチャットが出ない | BeatSaberPlus **Chat** モジュールが有効か確認 |
| `!bsr` が効かない | **ChatRequest** モジュールも有効にする必要あり |

### 自分でビルド
Windows + .NET SDK 8 + .NET Framework 4.7.2 + Beat Saber（BSIPA・BeatSaberPlus・BSML 含む）が必要。`Directory.Build.props` の `BeatSaberDir`（または環境変数）をインストール先に設定。1.29.1 ビルドは `1.29.1\izunaisbot-bsp-1.29.1.csproj`、1.44.0 ビルドは `1.44.0\izunaisbot-bsp-1.44.0.csproj` を使用。
```cmd
dotnet build -c Release
:: ビルド後にゲームの Plugins フォルダへコピー
dotnet build -c Release /p:CopyToPlugins=true
```
