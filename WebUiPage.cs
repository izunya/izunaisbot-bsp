namespace IzunaisbotBSP
{
    /// <summary>
    /// 로컬 웹 UI 의 단일 HTML 페이지 (영어/한국어/일본어 드롭다운).
    /// C# verbatim 문자열이라 큰따옴표는 피하고 HTML 속성/JS 문자열은 작은따옴표·백틱으로만 작성.
    /// </summary>
    public static class WebUiPage
    {
        public const string Html = @"<!doctype html>
<html lang='en' data-bs-theme='dark'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>izunaisbot Discord Bridge</title>
<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css' rel='stylesheet'>
<script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js'></script>
<style>
 body{padding-bottom:48px}
 .log{font-family:ui-monospace,Consolas,monospace;font-size:.84rem;max-height:380px;overflow-y:auto}
 .log .row-f{padding:1px 0}
 .log .muted{opacity:.4}
 code{color:#7aa2f7}
</style>
</head>
<body class='bg-dark'>
 <nav class='navbar bg-body-tertiary border-bottom'>
  <div class='container'>
   <span class='navbar-brand mb-0'>🟦 izunaisbot Discord Bridge</span>
   <span>
    <div class='dropdown d-inline-block me-2'>
     <button id='langbtn' class='btn btn-sm btn-outline-secondary dropdown-toggle' type='button' data-bs-toggle='dropdown' aria-expanded='false'>🌐 EN</button>
     <ul id='langmenu' class='dropdown-menu dropdown-menu-end'></ul>
    </div>
    <span id='conn' class='badge text-bg-secondary'>…</span>
   </span>
  </div>
 </nav>

 <div class='container mt-4'>
  <div id='update-banner' class='alert alert-warning py-2 mb-3' style='display:none'>
   <div class='d-flex justify-content-between align-items-center'>
    <span><span data-i18n='update.text'></span> <strong id='update-version'></strong></span>
    <a id='update-link' target='_blank' rel='noopener' class='btn btn-sm btn-warning' data-i18n='update.btn'></a>
   </div>
  </div>
  <div class='row g-4'>

   <div class='col-lg-6'>
    <div class='card'>
     <div class='card-header' data-i18n='card.connection'></div>
     <div class='card-body'>
      <div class='alert alert-info py-2 mb-3' id='pair-banner' style='display:none'>
       <div class='d-flex justify-content-between align-items-center'>
        <span data-i18n='pair.bannerHint'></span>
        <button class='btn btn-sm btn-light' onclick='startPair()' data-i18n='pair.startBtn'></button>
       </div>
      </div>
      <div class='alert alert-success py-2 mb-3' id='pair-status' style='display:none'>
       <div data-i18n='pair.codeLabel' class='small'></div>
       <div class='d-flex justify-content-between align-items-center mt-1'>
        <code id='pair-code' class='fs-4'></code>
        <a id='pair-link' target='_blank' rel='noopener' class='btn btn-sm btn-primary' data-i18n='pair.openBtn' onclick='return openPairDashboard(event)'></a>
       </div>
       <div class='small mt-2 text-muted' id='pair-msg'></div>
      </div>

      <div class='card border-success mb-3' id='pair-issue' style='display:none'>
       <div class='card-header bg-success-subtle py-2 small'>
        <span data-i18n='pair.issueHeader'></span>
       </div>
       <div class='card-body p-3'>
        <label class='form-label small' data-i18n='pair.tokenNameLabel'></label>
        <input id='pair-name' type='text' class='form-control form-control-sm mb-3' />
        <div class='small text-muted mb-1' data-i18n='pair.pickChannels'></div>
        <div id='pair-guilds' class='border rounded p-2 mb-3' style='max-height:240px;overflow-y:auto;font-size:.85rem'></div>
        <div id='pair-no-guilds' class='alert alert-warning small p-2 mb-3' style='display:none'></div>
        <div class='d-flex align-items-center gap-2'>
         <button id='pair-issue-btn' class='btn btn-success btn-sm' onclick='doIssue()' data-i18n='pair.issueBtn'></button>
         <button class='btn btn-link btn-sm text-muted' onclick='cancelPair()' data-i18n='pair.cancelBtn'></button>
         <span id='pair-issue-msg' class='ms-2 small text-danger'></span>
        </div>
       </div>
      </div>
      <div class='mb-3'>
       <label class='form-label' data-i18n='label.token'></label>
       <div class='input-group'>
        <input id='token' type='password' class='form-control' placeholder='bsp_...'>
        <button class='btn btn-outline-secondary' type='button' id='showbtn' onclick='toggleTok()' data-i18n='btn.show'></button>
       </div>
      </div>
      <div class='row'>
       <div class='col-7 mb-3'>
        <div class='form-check'>
         <input class='form-check-input' type='checkbox' id='auto'>
         <label class='form-check-label' for='auto' data-i18n='label.auto'></label>
        </div>
        <div class='form-check'>
         <input class='form-check-input' type='checkbox' id='cmdonly'>
         <label class='form-check-label' for='cmdonly' data-i18n='label.cmdonly'></label>
        </div>
        <div class='form-check'>
         <input class='form-check-input' type='checkbox' id='launch'>
         <label class='form-check-label' for='launch' data-i18n='label.launch'></label>
        </div>
       </div>
       <div class='col-5 mb-3'>
        <label class='form-label' data-i18n='label.interval'></label>
        <input id='ivl' type='number' min='1' class='form-control'>
       </div>
      </div>
      <button class='btn btn-primary' onclick='save()' data-i18n='btn.save'></button>
      <span id='saved' class='ms-2 text-success small'></span>
     </div>
    </div>

    <div class='card mt-4'>
     <div class='card-header' data-i18n='card.status'></div>
     <div class='card-body' id='status'>…</div>
    </div>
   </div>

   <div class='col-lg-6'>
    <div class='card'>
     <div class='card-header d-flex justify-content-between align-items-center'>
      <span data-i18n='card.channels'></span><span class='text-secondary small' data-i18n='ch.toggle'></span>
     </div>
     <div class='card-body p-2'>
      <table class='table table-sm align-middle mb-0'><tbody id='channels'></tbody></table>
     </div>
    </div>

    <div class='card mt-4 border-success-subtle'>
     <div class='card-header d-flex justify-content-between align-items-center'>
      <span>🟢 <span data-i18n='card.chzzk'></span></span>
      <span id='chzzk-conn' class='badge text-bg-secondary'>…</span>
     </div>
     <div class='card-body'>
      <div class='form-check form-switch mb-3'>
       <input class='form-check-input' type='checkbox' id='chzzk-enable'>
       <label class='form-check-label' for='chzzk-enable' data-i18n='chzzk.enable'></label>
      </div>
      <div class='mb-2'>
       <label class='form-label' data-i18n='chzzk.channelId'></label>
       <input id='chzzk-channel' type='text' class='form-control' placeholder='e.g. 0ac4xxxxxxxxxxxxxxxxxxxxxxxxxxxx'>
       <div class='form-text' data-i18n='chzzk.channelHint'></div>
      </div>
      <button class='btn btn-success btn-sm' onclick='saveChzzk()' data-i18n='chzzk.save'></button>
      <span id='chzzk-saved' class='ms-2 text-success small'></span>
      <div class='mt-3 small' id='chzzk-status'>…</div>
      <div class='mt-2'>
       <div class='d-flex justify-content-between align-items-center mb-1'>
        <span class='text-secondary small' data-i18n='chzzk.preview'></span>
        <button class='btn btn-sm btn-outline-secondary py-0' onclick='clearChzzk()' data-i18n='btn.clear'></button>
       </div>
       <div class='log' id='chzzk-log' style='max-height:200px'></div>
      </div>
     </div>
    </div>
   </div>

  </div>

  <div class='card mt-4'>
   <div class='card-header d-flex justify-content-between align-items-center'>
    <span><span data-i18n='card.log'></span> <span class='text-secondary small' data-i18n='log.sub'></span></span>
    <button class='btn btn-sm btn-outline-secondary' onclick='clearLog()' data-i18n='btn.clear'></button>
   </div>
   <div class='card-body log' id='log'></div>
  </div>
 </div>

<script>
const $=id=>document.getElementById(id);
let inited=false;
let lastServerToken=null;
const LANGS=['en','ko','ja'];
const LANGNAME={en:'English',ko:'한국어',ja:'日本語'};
let lang=localStorage.getItem('lang')||'en';
if(LANGS.indexOf(lang)<0)lang='en';

const S={
 en:{
  'conn.connected':'Connected','conn.disconnected':'Disconnected','conn.noresp':'No response',
  'card.connection':'Connection','card.status':'Status','card.channels':'Channels','card.log':'Message Log',
  'label.url':'WebSocket URL','label.token':'Token','btn.show':'Show',
  'label.auto':'Auto reconnect','label.cmdonly':'Forward only commands (!)','label.launch':'Open web on Beat Saber launch',
  'label.interval':'Reconnect interval (sec)','btn.save':'Save & Reconnect','msg.saved':'Saved ✓',
  'status.ws':'WebSocket','status.token':'Token','status.tokenSet':'set','status.tokenNone':'not set',
  'status.last':'Last message','btn.reconnect':'Reconnect now','time.now':'just now','time.sec':'s ago','time.min':'m ago','time.hour':'h ago',
  'ch.toggle':'forward on/off','ch.none':'No channels received yet','log.sub':'(newest first · filtered are dimmed)',
  'btn.clear':'Clear','log.filtered':'(filtered)','log.none':'No log',
  'update.text':'A new mod version is available:','update.btn':'Open release',
  'card.chzzk':'Chzzk chat','chzzk.enable':'Forward Chzzk chat into the game',
  'chzzk.channelId':'Channel ID','chzzk.channelHint':'The ID in your channel URL: chzzk.naver.com/<this part>',
  'chzzk.save':'Save & Reconnect','chzzk.preview':'Chat preview (newest first)',
  'chzzk.connected':'Connected','chzzk.disconnected':'Disconnected','chzzk.disabled':'Off',
  'chzzk.channelLabel':'Channel','chzzk.liveLabel':'Live','chzzk.statusLabel':'Status','chzzk.lastLabel':'Last message',
  'pair.bannerHint':'No token yet?','pair.startBtn':'Get from bot',
  'pair.codeLabel':'Enter this 6-digit code on the bot dashboard, then approve',
  'pair.openBtn':'Open dashboard',
  'pair.waiting':'Waiting for you to approve on the dashboard...',
  'pair.received':'✓ Token received. Saving...',
  'pair.expired':'Timed out. Try again.',
  'pair.failed':'Pairing failed',
  'pair.issueHeader':'Approved — pick channels & issue the token here',
  'pair.tokenNameLabel':'Token name (e.g. main PC)',
  'pair.pickChannels':'Pick the Discord channels to forward into the game:',
  'pair.issueBtn':'Issue token','pair.cancelBtn':'Cancel',
  'pair.loadingGuilds':'Loading channels...','pair.noGuilds':'No eligible servers found.',
  'pair.issuing':'Issuing...',
  'pair.errNameRequired':'Token name required','pair.errNoChannels':'Pick at least one channel',
  'pair.noGuildsTitle':'No servers with the bot available',
  'pair.inviteHint':'You manage these servers, but the bot is not in them. Invite it:',
  'pair.noManageableHint':'You do not manage any Discord server. Create one (or get the Manage Server permission), then click Recheck.',
  'pair.inviteToBtn':'Invite to',
  'pair.inviteGeneralBtn':'Invite (pick during OAuth)',
  'pair.recheckBtn':'Recheck',
  'btn.test':'Test bridge','test.ok':'✓ Posted to Discord','test.fail':'✗ Failed','test.pending':'sending...','test.detail':'detail'
 },
 ko:{
  'conn.connected':'연결됨','conn.disconnected':'끊김','conn.noresp':'웹 응답 없음',
  'card.connection':'연결 설정','card.status':'상태','card.channels':'채널','card.log':'메시지 로그',
  'label.url':'WebSocket URL','label.token':'토큰','btn.show':'표시',
  'label.auto':'자동 재접속','label.cmdonly':'명령어(!)만 게임으로 전달','label.launch':'비트세이버 실행 시 웹 열기',
  'label.interval':'재접속 간격(초)','btn.save':'저장 & 재접속','msg.saved':'저장됨 ✓',
  'status.ws':'WebSocket','status.token':'토큰','status.tokenSet':'설정됨','status.tokenNone':'없음',
  'status.last':'마지막 수신','btn.reconnect':'지금 재접속','time.now':'방금','time.sec':'초 전','time.min':'분 전','time.hour':'시간 전',
  'ch.toggle':'게임 전달 on/off','ch.none':'아직 수신된 채널이 없습니다','log.sub':'(신규순 · 필터된 메시지는 흐리게)',
  'btn.clear':'지우기','log.filtered':'(필터됨)','log.none':'로그 없음',
  'update.text':'새 모드 버전이 있습니다:','update.btn':'릴리스 열기',
  'card.chzzk':'치지직 채팅','chzzk.enable':'치지직 채팅을 게임으로 전달',
  'chzzk.channelId':'채널 ID','chzzk.channelHint':'채널 URL 의 ID: chzzk.naver.com/<이 부분>',
  'chzzk.save':'저장 & 재접속','chzzk.preview':'채팅 미리보기 (신규순)',
  'chzzk.connected':'연결됨','chzzk.disconnected':'끊김','chzzk.disabled':'꺼짐',
  'chzzk.channelLabel':'채널','chzzk.liveLabel':'방송','chzzk.statusLabel':'상태','chzzk.lastLabel':'마지막 수신',
  'pair.bannerHint':'아직 토큰이 없으신가요?','pair.startBtn':'봇에서 가져오기',
  'pair.codeLabel':'봇 대시보드에서 이 6자리 코드를 입력하고 승인하세요',
  'pair.openBtn':'대시보드 열기',
  'pair.waiting':'대시보드에서 승인하기를 기다리는 중...',
  'pair.received':'✓ 토큰을 받았습니다. 저장 중...',
  'pair.expired':'시간 초과. 다시 시도해주세요.',
  'pair.failed':'페어링 실패',
  'pair.issueHeader':'승인 완료 — 여기서 채널 선택 + 토큰 발급',
  'pair.tokenNameLabel':'토큰 이름 (예: 메인 PC)',
  'pair.pickChannels':'게임으로 전달할 디스코드 채널을 선택하세요:',
  'pair.issueBtn':'토큰 발급','pair.cancelBtn':'취소',
  'pair.loadingGuilds':'채널 목록 불러오는 중...','pair.noGuilds':'사용 가능한 서버가 없습니다.',
  'pair.issuing':'발급 중...',
  'pair.errNameRequired':'토큰 이름이 필요합니다','pair.errNoChannels':'채널을 1개 이상 선택하세요',
  'pair.noGuildsTitle':'봇이 들어가 있는 서버가 없습니다',
  'pair.inviteHint':'아래 서버는 관리 권한이 있지만 봇이 없습니다. 초대하세요:',
  'pair.noManageableHint':'관리 권한이 있는 Discord 서버가 없습니다. 서버를 만들거나 서버 관리 권한을 받은 뒤 [다시 확인]을 눌러주세요.',
  'pair.inviteToBtn':'초대',
  'pair.inviteGeneralBtn':'봇 초대 (OAuth 화면에서 서버 선택)',
  'pair.recheckBtn':'다시 확인',
  'btn.test':'브리지 테스트','test.ok':'✓ 디스코드에 게시됨','test.fail':'✗ 실패','test.pending':'전송 중...','test.detail':'상세'
 },
 ja:{
  'conn.connected':'接続済み','conn.disconnected':'切断','conn.noresp':'応答なし',
  'card.connection':'接続設定','card.status':'ステータス','card.channels':'チャンネル','card.log':'メッセージログ',
  'label.url':'WebSocket URL','label.token':'トークン','btn.show':'表示',
  'label.auto':'自動再接続','label.cmdonly':'コマンド(!)のみ転送','label.launch':'起動時にWebを開く',
  'label.interval':'再接続間隔(秒)','btn.save':'保存して再接続','msg.saved':'保存しました ✓',
  'status.ws':'WebSocket','status.token':'トークン','status.tokenSet':'設定済み','status.tokenNone':'未設定',
  'status.last':'最終受信','btn.reconnect':'今すぐ再接続','time.now':'たった今','time.sec':'秒前','time.min':'分前','time.hour':'時間前',
  'ch.toggle':'転送 on/off','ch.none':'受信したチャンネルがありません','log.sub':'(新しい順・フィルタ済みは薄く)',
  'btn.clear':'クリア','log.filtered':'(フィルタ済み)','log.none':'ログなし',
  'update.text':'新しい MOD バージョンがあります:','update.btn':'リリースを開く',
  'card.chzzk':'Chzzk チャット','chzzk.enable':'Chzzk チャットをゲームへ転送',
  'chzzk.channelId':'チャンネルID','chzzk.channelHint':'チャンネルURLのID: chzzk.naver.com/<この部分>',
  'chzzk.save':'保存して再接続','chzzk.preview':'チャットプレビュー (新しい順)',
  'chzzk.connected':'接続済み','chzzk.disconnected':'切断','chzzk.disabled':'オフ',
  'chzzk.channelLabel':'チャンネル','chzzk.liveLabel':'配信','chzzk.statusLabel':'ステータス','chzzk.lastLabel':'最終受信',
  'pair.bannerHint':'まだトークンがありませんか?','pair.startBtn':'ボットから取得',
  'pair.codeLabel':'ボットダッシュボードでこの 6 桁コードを入力して承認してください',
  'pair.openBtn':'ダッシュボードを開く',
  'pair.waiting':'ダッシュボードでの承認を待っています...',
  'pair.received':'✓ トークンを受信しました。保存中...',
  'pair.expired':'タイムアウト。もう一度お試しください。',
  'pair.failed':'ペアリング失敗',
  'pair.issueHeader':'承認完了 — ここでチャンネル選択とトークン発行',
  'pair.tokenNameLabel':'トークン名 (例: メイン PC)',
  'pair.pickChannels':'ゲームに転送する Discord チャンネルを選択してください:',
  'pair.issueBtn':'トークン発行','pair.cancelBtn':'キャンセル',
  'pair.loadingGuilds':'チャンネル一覧読込中...','pair.noGuilds':'利用可能なサーバーがありません。',
  'pair.issuing':'発行中...',
  'pair.errNameRequired':'トークン名が必要です','pair.errNoChannels':'チャンネルを 1 つ以上選択してください',
  'pair.noGuildsTitle':'ボットが入っているサーバーがありません',
  'pair.inviteHint':'管理権限のあるサーバーですがボットがいません。招待してください:',
  'pair.noManageableHint':'管理権限のある Discord サーバーがありません。サーバーを作成するか管理権限を取得してから [再確認] を押してください。',
  'pair.inviteToBtn':'招待',
  'pair.inviteGeneralBtn':'ボットを招待 (OAuth で選択)',
  'pair.recheckBtn':'再確認',
  'btn.test':'ブリッジ テスト','test.ok':'✓ Discord に投稿','test.fail':'✗ 失敗','test.pending':'送信中...','test.detail':'詳細'
 }
};
function t(k){return (S[lang]&&S[lang][k])||S.en[k]||k;}

function esc(s){return (s==null?'':String(s)).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/'/g,'&#39;');}
function ago(iso){const d=(Date.now()-new Date(iso).getTime())/1000;if(d<5)return t('time.now');if(d<60)return Math.floor(d)+t('time.sec');if(d<3600)return Math.floor(d/60)+t('time.min');return Math.floor(d/3600)+t('time.hour');}
function toggleTok(){const x=$('token');x.type=x.type==='password'?'text':'password';}

function applyI18n(){
 document.querySelectorAll('[data-i18n]').forEach(e=>{e.textContent=t(e.dataset.i18n);});
 document.documentElement.lang=lang;
 $('langbtn').textContent='🌐 '+lang.toUpperCase();
 renderLangMenu();
 poll();
}
function renderLangMenu(){
 const m=$('langmenu'); if(!m) return;
 m.innerHTML=LANGS.map(L=>{
  const sel=(L===lang)?' active':'';
  const mark=(L===lang)?'✓ ':'';
  return `<li><button type='button' class='dropdown-item${sel}' onclick='setLang(&#39;${L}&#39;)'>${mark}${esc(LANGNAME[L])}</button></li>`;
 }).join('');
}
function setLang(L){ if(LANGS.indexOf(L)<0) return; lang=L; localStorage.setItem('lang',lang); applyI18n(); }

async function poll(){
 try{
  const s=await(await fetch('/api/state')).json();
  render(s);
 }catch(e){
  const c=$('conn');c.className='badge text-bg-danger';c.textContent=t('conn.noresp');
 }
}

function render(s){
 if(!inited){
  $('token').value=s.token||'';
  $('auto').checked=!!s.autoReconnect;
  $('ivl').value=s.reconnectIntervalSec||10;
  $('cmdonly').checked=!!s.forwardOnlyCommands;
  $('launch').checked=!!s.openWebOnLaunch;
  $('chzzk-enable').checked=!!s.chzzkEnabled;
  $('chzzk-channel').value=s.chzzkChannelId||'';
  inited=true;
 } else if(s.token!==lastServerToken && document.activeElement!==$('token')){
  $('token').value=s.token||'';
 }
 lastServerToken=s.token;
 pairBotBase=s.botApiBase||pairBotBase;
 // 새 버전 알림 배너 — 최신 tag 받았고 자기 버전보다 크면 표시
 const ub=$('update-banner');
 if(s.updateAvailable && s.latestVersion){
  $('update-version').textContent=s.latestVersion;
  $('update-link').href=s.latestUrl||'https://github.com/izunya/izunaisbot-bsp/releases';
  ub.style.display='block';
 } else {
  ub.style.display='none';
 }
 const issueOpen=$('pair-issue').style.display==='block';
 const statusOpen=$('pair-status').style.display==='block';
 if(!issueOpen && !statusOpen){
  $('pair-banner').style.display=s.tokenSet?'none':'block';
 }
 if(s.tokenSet){
  $('pair-banner').style.display='none';
 }
 const c=$('conn');
 c.className='badge '+(s.connected?'text-bg-success':'text-bg-danger');
 c.textContent=s.connected?t('conn.connected'):t('conn.disconnected');

 let testHtml='';
 const bt=s.bridgeTest||{};
 if(bt.sentUtc){
  if(bt.ackUtc){
   const cls=bt.ok?'text-success':'text-danger';
   const lbl=bt.ok?t('test.ok'):t('test.fail');
   const det=bt.detail?` <span class='text-secondary'>(${t('test.detail')}: ${esc(bt.detail)})</span>`:'';
   testHtml=` <span class='${cls}'>${lbl}</span>${det}`;
  } else {
   testHtml=` <span class='text-warning'>${t('test.pending')}</span>`;
  }
 }
 $('status').innerHTML=
  `<div class='mb-1'>${t('status.ws')}: <code>${esc(s.url||'-')}</code></div>`+
  `<div class='mb-1'>${t('status.token')}: ${s.tokenSet?`<span class='text-success'>${t('status.tokenSet')}</span>`:`<span class='text-warning'>${t('status.tokenNone')}</span>`}</div>`+
  `<div class='mb-2'>${t('status.last')}: ${s.lastMessageUtc?ago(s.lastMessageUtc):'-'}</div>`+
  `<button class='btn btn-sm btn-outline-light me-2' onclick='reconnect()'>${t('btn.reconnect')}</button>`+
  `<button class='btn btn-sm btn-outline-info' onclick='testBridge()' ${s.connected?'':'disabled'}>${t('btn.test')}</button>`+
  testHtml;

 const ch=s.channels||[];
 $('channels').innerHTML= ch.length? ch.map(x=>
  `<tr><td>${esc(x.name)}</td>`+
  `<td class='text-secondary text-end' style='width:70px'>${x.count}</td>`+
  `<td class='text-end' style='width:60px'><div class='form-check form-switch d-inline-block'>`+
  `<input class='form-check-input chtoggle' type='checkbox' data-id='${x.id}' ${x.enabled?'checked':''}></div></td></tr>`
 ).join('') : `<tr><td class='text-secondary'>${t('ch.none')}</td></tr>`;
 document.querySelectorAll('.chtoggle').forEach(el=>{el.onchange=()=>toggle(el.dataset.id,el.checked);});

 const lg=s.log||[];
 $('log').innerHTML= lg.length? lg.map(m=>
  `<div class='row-f ${m.forwarded?'':'muted'}'>`+
  `<span class='text-secondary'>${m.time}</span> `+
  `<span class='text-info'>${esc(m.channel)}</span> `+
  `<b>${esc(m.user)}</b>: ${esc(m.content)}`+
  `${m.forwarded?'':` <span class='text-warning'>${t('log.filtered')}</span>`}</div>`
 ).join('') : `<div class='text-secondary'>${t('log.none')}</div>`;

 renderChzzk(s);
}

function renderChzzk(s){
 const badge=$('chzzk-conn');
 if(!s.chzzkEnabled){ badge.className='badge text-bg-secondary'; badge.textContent=t('chzzk.disabled'); }
 else { badge.className='badge '+(s.chzzkConnected?'text-bg-success':'text-bg-danger'); badge.textContent=s.chzzkConnected?t('chzzk.connected'):t('chzzk.disconnected'); }

 $('chzzk-status').innerHTML=
  `<div>${t('chzzk.channelLabel')}: <b>${esc(s.chzzkChannelName||'-')}</b></div>`+
  `<div>${t('chzzk.liveLabel')}: ${esc(s.chzzkLiveTitle||'-')}</div>`+
  `<div>${t('chzzk.statusLabel')}: <span class='text-secondary'>${esc(s.chzzkStatus||'-')}</span></div>`+
  `<div>${t('chzzk.lastLabel')}: ${s.chzzkLastMessageUtc?ago(s.chzzkLastMessageUtc):'-'}</div>`;

 const cl=s.chzzkLog||[];
 $('chzzk-log').innerHTML= cl.length? cl.map(m=>
  `<div class='row-f ${m.forwarded?'':'muted'}'><span class='text-secondary'>${m.time}</span> <b>${esc(m.user)}</b>: ${esc(m.content)}`+
  `${m.forwarded?'':` <span class='text-warning'>${t('log.filtered')}</span>`}</div>`
 ).join('') : `<div class='text-secondary'>${t('log.none')}</div>`;
}

async function saveChzzk(){
 const body={chzzkEnabled:$('chzzk-enable').checked,chzzkChannelId:$('chzzk-channel').value.trim()};
 await fetch('/api/chzzk/config',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 const sv=$('chzzk-saved');sv.textContent=t('msg.saved');setTimeout(()=>sv.textContent='',2000);
 poll();
}
async function clearChzzk(){
 await fetch('/api/chzzk/clearlog',{method:'POST'});
 poll();
}

async function save(){
 const body={token:$('token').value.trim(),autoReconnect:$('auto').checked,reconnectIntervalSec:parseInt($('ivl').value)||10,forwardOnlyCommands:$('cmdonly').checked,openWebOnLaunch:$('launch').checked};
 await fetch('/api/config',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
 const sv=$('saved');sv.textContent=t('msg.saved');setTimeout(()=>sv.textContent='',2000);
 poll();
}
async function reconnect(){
 await fetch('/api/config',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});
 poll();
}
async function testBridge(){
 try { await fetch('/api/test-bridge',{method:'POST'}); } catch(_){}
 poll();
 // ack 가 1-3초 안에 오므로 짧게 다시 poll
 setTimeout(poll, 1500);
 setTimeout(poll, 3500);
}
async function toggle(id,en){
 await fetch('/api/channel',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({id:id,enabled:en})});
}
async function clearLog(){
 await fetch('/api/clearlog',{method:'POST'});
 poll();
}

// ---- 페어링 ----
let pairBotBase=null;
let pairPolling=null;
let pairSessionId=null;

function botFetch(path, opts){
 return fetch(pairBotBase.replace(/\/$/,'')+path, opts);
}

async function startPair(){
 if(!pairBotBase){ $('pair-msg').textContent=t('pair.failed')+': botApiBase'; return; }
 try{
  const r=await botFetch('/api/bsp-bridge/pair/start',{method:'POST'});
  const j=await r.json();
  if(!r.ok||!j.sessionId) throw new Error(j.error||('http '+r.status));
  pairSessionId=j.sessionId;
  $('pair-code').textContent=j.code;
  $('pair-link').href=j.claimUrl;
  $('pair-banner').style.display='none';
  $('pair-status').style.display='block';
  $('pair-issue').style.display='none';
  $('pair-msg').textContent=t('pair.waiting');
  pollPair(j.sessionId, j.expiresAt);
 }catch(e){
  $('pair-status').style.display='block';
  $('pair-msg').textContent=t('pair.failed')+': '+(e.message||e);
 }
}

// 'Open dashboard' 클릭 — window.open() 으로 띄워야 봇 대시보드가 승인 완료 후
// 스스로 창을 닫을 수 있다 (브라우저는 JS 가 연 창만 self-close 허용).
// 일반 click 만 가로채고 middle/ctrl-click 은 href 그대로 (브라우저 기본 동작).
// noopener 는 일부러 안 줌 — noopener 창은 auxiliary 가 아니라서 히스토리가 1개일
// 때만 window.close() 가 먹는다. 로그인 리다이렉트로 히스토리가 늘면 자동 닫기가
// 실패하므로, auxiliary 창으로 열어 항상 닫히게 한다. 대시보드는 본인 신뢰 도메인.
function openPairDashboard(ev){
 if(ev && (ev.button !== 0 || ev.ctrlKey || ev.metaKey || ev.shiftKey)) return true;
 if(ev) ev.preventDefault();
 const url=$('pair-link').href;
 if(url) window.open(url, '_blank');
 return false;
}

function pollPair(sessionId, expiresAt){
 if(pairPolling) clearInterval(pairPolling);
 pairPolling=setInterval(async()=>{
  if(Date.now()>expiresAt){
   stopPolling();
   $('pair-msg').textContent=t('pair.expired');
   return;
  }
  try{
   const r=await botFetch('/api/bsp-bridge/pair/'+encodeURIComponent(sessionId));
   const j=await r.json();
   if(j.status==='authenticated'){
    stopPolling();
    showIssuePanel();
    loadGuilds(sessionId);
   }else if(j.status==='issued'){
    stopPolling();
    await sendTokenToMod(j.rawToken, j.wsUrl);
   }else if(j.status==='expired'){
    stopPolling();
    $('pair-msg').textContent=t('pair.expired');
   }
  }catch{}
 }, 1500);
}

function stopPolling(){
 if(pairPolling){ clearInterval(pairPolling); pairPolling=null; }
}

function showIssuePanel(){
 $('pair-msg').textContent='';
 $('pair-status').style.display='none';
 $('pair-issue').style.display='block';
 $('pair-guilds').textContent=t('pair.loadingGuilds');
 $('pair-no-guilds').style.display='none';
}

async function loadGuilds(sessionId){
 try{
  const r=await botFetch('/api/bsp-bridge/pair/'+encodeURIComponent(sessionId)+'/guilds',{method:'POST'});
  const j=await r.json();
  if(!r.ok) throw new Error(j.error||('http '+r.status));
  renderGuilds(j);
 }catch(e){
  $('pair-guilds').textContent=t('pair.failed')+': '+(e.message||e);
  $('pair-no-guilds').style.display='none';
 }
}

function renderGuilds(payload){
 const guilds=(payload&&payload.guilds)||[];
 const without=(payload&&payload.manageableWithoutBot)||[];
 const generalInvite=(payload&&payload.inviteUrl)||null;
 const noBox=$('pair-no-guilds');
 const gBox=$('pair-guilds');
 if(guilds.length){
  noBox.style.display='none';
  const html=guilds.map(g=>
   `<div class='mb-2'><div class='fw-semibold small text-secondary'>${esc(g.name)}</div>`+
   `<div class='d-flex flex-wrap gap-1 mt-1'>`+
    (g.channels||[]).map(c=>{
     const key=g.id+':'+c.id;
     return `<label class='btn btn-sm btn-outline-secondary'><input type='checkbox' class='form-check-input me-1 pair-ch' value='${esc(key)}'>#${esc(c.name)}</label>`;
    }).join('')+
   `</div></div>`
  ).join('');
  gBox.innerHTML=html;
  return;
 }
 gBox.innerHTML='';
 let html=`<div class='mb-2 fw-semibold'>${esc(t('pair.noGuildsTitle'))}</div>`;
 if(without.length){
  html+=`<div class='mb-2 small'>${esc(t('pair.inviteHint'))}</div>`;
  html+=`<div class='d-flex flex-wrap gap-1 mb-2'>`+without.map(g=>
   `<a class='btn btn-sm btn-outline-light' target='_blank' rel='noopener' href='${esc(g.inviteUrl)}'>${esc(t('pair.inviteToBtn'))}: ${esc(g.name)}</a>`
  ).join('')+`</div>`;
 } else {
  html+=`<div class='mb-2 small'>${esc(t('pair.noManageableHint'))}</div>`;
 }
 if(generalInvite){
  html+=`<a class='btn btn-sm btn-primary me-2' target='_blank' rel='noopener' href='${esc(generalInvite)}'>${esc(t('pair.inviteGeneralBtn'))}</a>`;
 }
 html+=`<button type='button' class='btn btn-sm btn-outline-secondary' onclick='loadGuilds(pairSessionId)'>${esc(t('pair.recheckBtn'))}</button>`;
 noBox.innerHTML=html;
 noBox.style.display='block';
}

async function doIssue(){
 const name=($('pair-name').value||'').trim();
 const picks=Array.from(document.querySelectorAll('.pair-ch')).filter(el=>el.checked).map(el=>{
  const [guildId,channelId]=el.value.split(':');
  return {guildId, channelId};
 });
 $('pair-issue-msg').textContent='';
 if(!name){ $('pair-issue-msg').textContent=t('pair.errNameRequired'); return; }
 if(!picks.length){ $('pair-issue-msg').textContent=t('pair.errNoChannels'); return; }
 const btn=$('pair-issue-btn');
 const orig=btn.textContent; btn.disabled=true; btn.textContent=t('pair.issuing');
 try{
  const r=await botFetch('/api/bsp-bridge/pair/'+encodeURIComponent(pairSessionId)+'/issue',{
   method:'POST', headers:{'Content-Type':'application/json'},
   body:JSON.stringify({name, sources:picks})
  });
  const j=await r.json();
  if(!r.ok||!j.rawToken) throw new Error(j.error||('http '+r.status));
  await sendTokenToMod(j.rawToken, j.wsUrl);
 }catch(e){
  $('pair-issue-msg').textContent=t('pair.failed')+': '+(e.message||e);
  btn.disabled=false; btn.textContent=orig;
 }
}

async function sendTokenToMod(rawToken, wsUrl){
 await fetch('/api/pair-receive',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({token:rawToken, wsUrl:wsUrl})});
 $('pair-issue').style.display='none';
 $('pair-status').style.display='block';
 $('pair-msg').textContent=t('pair.received');
 setTimeout(()=>{ $('pair-status').style.display='none'; poll(); }, 1500);
}

function cancelPair(){
 stopPolling();
 pairSessionId=null;
 $('pair-issue').style.display='none';
 $('pair-status').style.display='none';
 $('pair-no-guilds').style.display='none';
 poll();
}

applyI18n();
setInterval(poll,1500);
</script>
</body>
</html>";
    }
}
