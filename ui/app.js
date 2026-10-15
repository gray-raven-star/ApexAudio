import {GroupTree} from './tree.js?v=20261014-categories';
import {Timeline} from './timeline.js?v=20261015-waveform';
import {SettingsPanel} from './settings.js';

const $ = id => document.getElementById(id);
const send = (action,values={}) => window.chrome.webview.postMessage({action,...values});
const settingsPanel = new SettingsPanel(send);
let state = {}, selected = null, trackId = null, currentPage = 'workspace', activity = {}, audibleSnapshot = null, packId = null;
let audio = new Audio(), audioUrl = null, clipRequest = null, lastTake = null, toastTimer;
function toast(text) { $('toast').textContent=text; $('toast').hidden=false; clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').hidden=true,2600); }
const tree = new GroupTree($('tree'), (action,id,enabled)=>send(action,{id,enabled}), g => { selected=g.id; showDetail(); if(state.take?.tracks.some(t=>t.groupId===g.id)) selectTrack(g.id); }, async folder => {
  const result = await ask('编辑显示文件夹',folder.name,true);
  if(typeof result==='string' && result) send('editFolder',{id:folder.id,name:result,delete:false});
  else if(result?.delete && confirm('删除这个显示文件夹？其中的声音会回到原来的位置。')) send('editFolder',{id:folder.id,name:'',delete:true});
});
const timeline = new Timeline($('timeline'), (start,end) => { $('rangeStart').value=start.toFixed(2); $('rangeEnd').value=end.toFixed(2); });
const savedAudioScale = localStorage.getItem('audioWaveformScale');
$('audioScale').value = ['auto','1','4','10','25','100'].includes(savedAudioScale) ? savedAudioScale : 'auto';
timeline.audioScale = $('audioScale').value;
timeline.displayChanged = ({gain,label}) => { $('audioScaleInfo').textContent = `本段共用 ${gain.toFixed(gain < 10 ? 1 : 0)}× · 峰值 ${label}`; };
$('audioScale').onchange = () => { timeline.audioScale = $('audioScale').value; localStorage.setItem('audioWaveformScale', timeline.audioScale); timeline.draw(); };
const savedScale = Number(localStorage.getItem('hapticCurveScale'));
$('hapticScale').value = String([1,4,10].includes(savedScale) ? savedScale : 4); timeline.gain = Number($('hapticScale').value);
$('hapticScale').onchange = () => { timeline.gain = Number($('hapticScale').value); localStorage.setItem('hapticCurveScale',$('hapticScale').value); timeline.draw(); };
const savedSource=localStorage.getItem('hapticCurveSource');
$('hapticSource').value=['both','supplement','final'].includes(savedSource)?savedSource:'both';timeline.hapticSource=$('hapticSource').value;
$('hapticSource').onchange=()=>{timeline.hapticSource=$('hapticSource').value;localStorage.setItem('hapticCurveSource',timeline.hapticSource);showHapticNote();timeline.draw();};
$('treeMode').value = localStorage.getItem('groupTreeMode') === 'hierarchy' ? 'hierarchy' : 'category';
tree.mode = $('treeMode').value;
$('treeMode').onchange = () => { localStorage.setItem('groupTreeMode', $('treeMode').value); tree.setMode($('treeMode').value); };
function ask(title,value='',allowDelete=false) {
  const dialog=$('textDialog'); $('dialogTitle').textContent=title; $('dialogInput').value=value; $('dialogDelete').hidden=!allowDelete; dialog.showModal(); $('dialogInput').focus(); $('dialogInput').select();
  return new Promise(resolve=>dialog.addEventListener('close',()=>resolve(dialog.returnValue==='ok'?$('dialogInput').value.trim():dialog.returnValue==='delete'?{delete:true}:null),{once:true}));
}
function safeToLeave() { return !state.dirty || confirm('当前预设有未保存修改，继续会丢弃这些修改。'); }
function options(select, items, value, placeholder) {
  const signature=JSON.stringify(items); if(select.dataset.signature!==signature) { select.replaceChildren(); if(!items.length&&placeholder) {const o=new Option(placeholder,'');select.add(o);} items.forEach(i=>select.add(new Option(i.name,i.id))); select.dataset.signature=signature; }
  select.value=value || '';
}
function page(name) {
  currentPage=name; document.querySelectorAll('.page').forEach(p=>p.hidden=p.id!==name); document.querySelectorAll('.nav').forEach(b=>b.classList.toggle('active',b.dataset.page===name));
  const titles={workspace:['声音工作台','游戏声音与手柄反馈','选择参与震动的声音；录制后可逐组试听，并对照震动时间。'],recordings:['录音与试听','录音与试听','选择录音，逐组试听声音，并对照震动时间轴。'],packages:['游戏适配包','游戏适配包','导入对应游戏的适配包，查看和管理已安装的包。'],settings:['应用设置','应用设置','调整连接、反馈、录制快捷键和界面主题。']};
  $('pageTitle').textContent=titles[name][0]; $('heading').textContent=titles[name][1]; $('subtitle').textContent=titles[name][2]; timeline.draw();
}
document.querySelectorAll('.nav').forEach(b=>b.onclick=()=>page(b.dataset.page));
function showDetail() {
  const group=state.groups?.find(g=>g.id===selected); $('detail').hidden=!group; $('detailEmpty').hidden=!!group;
  if(!group){$('pcmBadge').textContent='未选择';return;}
  const setting=state.preset?.groups[group.id] || {};
  $('groupName').textContent=setting.name || group.name;
  $('groupMeta').textContent=`${group.category} · 编号 ${group.id} · 名称来自${group.nameSource}`;
  $('pcmBadge').textContent=group.hasPcm ? (activity.verified?.includes(group.id)?'已收到独立音轨':'待收到音轨'):'无独立音轨';
  if(document.activeElement!==$('editName')) $('editName').value=setting.name || '';
  if(document.activeElement!==$('editFolder'))options($('editFolder'),[{id:'',name:'原始混音层级'},...(state.preset?.folders || []).map(f=>({id:f.id,name:f.name}))],setting.folderId || '');
  if(document.activeElement!==$('editOrder')) $('editOrder').value=setting.order || 0;
  $('routingNote').textContent=group.containsChildren?'这个游戏混音分组包含子分组；参与反馈时会避免同时叠加其子组。':'显示分类和名称只整理列表，不改变游戏音频。';
  $('listenSelected').disabled=!state.take?.tracks.some(t=>t.groupId===selected);
}
function selectTrack(id) { trackId=id; renderTracks(); const track=state.take?.tracks.find(t=>t.groupId===id); timeline.set(state.take,track); $('timelineTitle').textContent=track?.name || '声音与震动'; if(!audio.paused) play(); }
function renderTracks() {
  const take=state.take; if(!take)return;
  const filter=$('trackSearch').value.toLowerCase(), only=$('recordAudible').checked;
  const tracks=take.tracks.filter(t=>(!only || t.peaks.some(p=>p>.00003)) && (t.name+' '+t.groupId).toLowerCase().includes(filter));
  $('trackCount').textContent=tracks.length; $('tracks').replaceChildren();
  for(const track of tracks) {
    const row=document.createElement('div');row.className='track'+(track.groupId===trackId?' active':'');row.tabIndex=0;row.setAttribute('role','button');
    const info=document.createElement('div');info.className='track-info';const name=document.createElement('span');name.textContent=track.name;const sub=document.createElement('small');sub.textContent=track.groupId+' · 独立音轨';info.append(name,sub);
    const label=document.createElement('label');label.className='track-feedback';const toggle=document.createElement('input');toggle.type='checkbox';
    const group=state.groups?.find(g=>g.id===track.groupId);
    toggle.disabled=state.busy||!state.connected||state.packId!==take.packId||!group?.hasPcm;
    toggle.checked=state.packId===take.packId&&state.preset?.groups[track.groupId]?.enabled===true;
    toggle.setAttribute('aria-label',track.name+'参与反馈');label.title=toggle.disabled?'连接对应游戏后可修改反馈开关':'修改当前预设中该分组的反馈开关';
    const caption=document.createElement('span');caption.textContent='反馈';label.append(toggle,caption);label.onclick=e=>e.stopPropagation();label.onkeydown=e=>e.stopPropagation();
    toggle.onchange=()=>send('group',{id:track.groupId,enabled:toggle.checked});
    const rename=document.createElement('button');rename.className='track-rename';rename.textContent='✎';rename.title='修改音轨名称';rename.setAttribute('aria-label','修改 '+track.name+' 的名称');
    rename.onclick=async e=>{e.stopPropagation();const next=await ask('修改分组名称',track.name);if(next)send('renameTrack',{takeId:take.id,id:track.groupId,name:next});};rename.onkeydown=e=>e.stopPropagation();
    row.append(info,rename,label);row.onclick=()=>selectTrack(track.groupId);row.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();selectTrack(track.groupId);}};$('tracks').append(row);
  }
  if(!tracks.length){const p=document.createElement('p');p.className='empty small';p.textContent='没有符合条件的音轨';$('tracks').append(p);}
}
function showTake() {
  const take=state.take; $('takeEmpty').hidden=!!take; $('timelineContent').hidden=!take;
  if(!take)return;
  if(lastTake!==take.id){audio.pause();lastTake=take.id;trackId=take.tracks.find(t=>t.groupId===selected)?.groupId || take.tracks.find(t=>t.peaks.some(p=>p>.00003))?.groupId || take.tracks[0]?.groupId;}
  $('takeInfo').textContent=`${take.duration.toFixed(2)} 秒 · ${take.sampleRate/1000} kHz · ${take.tracks.length} 条音轨`;
  showHapticNote();
  timeline.set(take,take.tracks.find(t=>t.groupId===trackId)); $('timelineTitle').textContent=take.tracks.find(t=>t.groupId===trackId)?.name || '声音与震动'; renderTracks();
}
function showHapticNote() {
  const h=state.take?.haptics, hasSupplement=h?.blocks?.some(b=>b.supplementLeft!=null||b.supplementRight!=null||b.supplementTrigger!=null);
  const mode=timeline.hapticSource;
  $('supplementLegend').hidden=mode==='final';$('finalLegend').hidden=mode==='supplement';$('finalLegend').className=mode==='both'?'haptic-final-color':'haptic-color';
  const description=mode==='both'?'彩色为声音补充，灰色为引擎合成输出。':mode==='supplement'?'仅显示声音补充分量。':'仅显示引擎合成输出。';
  $('hapticNote').textContent=h?.blocks?.length
    ? description+(!hasSupplement&&mode!=='final'?' 这段录音没有单独的补充分量。':'')+' '+(h.note||'曲线为引擎输出数据，非马达实测。')
    : (h?.note || '本次没有 X-Haptics 震动记录，独立声音轨道仍可试听。');
}
function renderPackages() {
  const signature=JSON.stringify([state.packs,state.packId]); if($('packageList').dataset.signature===signature)return; $('packageList').dataset.signature=signature; $('packageList').replaceChildren();
  for(const pack of state.packs || []) {
    const card=document.createElement('div');card.className='card package-card';
    const heading=document.createElement('h3');heading.textContent=pack.name;
    const meta=document.createElement('div');meta.className='package-meta';meta.textContent=pack.version;
    const actions=document.createElement('div');actions.className='package-actions';const use=document.createElement('button');use.textContent=pack.id===state.packId?'当前适配包':'使用此包';use.onclick=()=>{if(safeToLeave()){send('selectPack',{id:pack.id});page('workspace');}};
    const remove=document.createElement('button');remove.className='subtle';remove.textContent='卸载';remove.onclick=()=>send('uninstall',{id:pack.id});actions.append(use,remove);card.append(heading,meta,actions);$('packageList').append(card);
  }
  if(!state.packs?.length){const p=document.createElement('div');p.className='card empty';p.textContent='还没有适配包，点击右上角导入 .gpack。';$('packageList').append(p);}
}
function recordButtons(phase) {
  for(const id of ['recordFromTimeline']){
    $(id).textContent=phase==='countdown'?'取消倒计时':phase==='recording'?'■ 结束录制':phase==='saving'?'正在保存…':'● 开始录制';
    $(id).disabled=state.busy||phase==='saving'||!state.connected||!state.capabilities?.independentPcm;
  }
}
function render() {
  if(packId!==state.packId){packId=state.packId;selected=null;tree.reset();}
  options($('packSelect'),(state.packs || []).map(p=>({id:p.id,name:p.name+(p.demo?' · 演示':'')})),state.packId,'导入游戏适配包开始使用');
  options($('presetSelect'),(state.presets || []).map(p=>({id:p.id,name:p.name})),state.preset?.id,'连接后获取作者预设');
  $('dirty').hidden=!state.dirty; $('saveTrackNames').hidden=!state.preset || state.take?.packId!==state.packId || !state.dirty; $('saveTrackNames').disabled=state.busy; $('connectionDot').classList.toggle('on',state.connected);$('connectionText').textContent=state.connected?'采集已连接':'采集未连接';$('footerStatus').textContent=state.status;$('busyText').textContent=state.busy?'正在处理…':'';
  document.querySelector('[data-menu=restore]').disabled=!state.preset?.basePresetId;
  document.querySelector('[data-menu=delete]').disabled=!state.preset?.id?.startsWith('user-');
  $('gameState').textContent=state.status;$('connect').textContent=state.connected?'断开采集':'连接采集';$('connect').disabled=state.busy||!state.packId;
  $('error').hidden=!state.error;$('errorText').textContent=state.error || '';
  for(const id of ['presetSelect','savePreset','saveAs','presetMore','addFolder','applyEdit']) $(id).disabled=state.busy||!state.preset;
  $('feedback').checked=!!state.settings?.autoFeedback;$('feedback').disabled=state.busy||!state.connected;
  $('feedbackState').textContent=state.feedbackDetail || '补充震动已关闭';
  recordButtons(state.recordingPhase);
  $('groupCount').textContent=state.groups?.length?`${state.groups.length} 个分组 · ${state.groups.filter(g=>g.hasPcm).length} 个可采集`:'等待适配器提供分组';
  if(state.groups?.length)tree.update(state.groups,state.preset);
  if(!state.groups?.length && tree.groups?.length){tree.groups=[];tree.element.replaceChildren();const empty=document.createElement('div');empty.className='empty';empty.textContent='连接后显示声音分组';tree.element.append(empty);}
  options($('takeSelect'),(state.recordings || []).map(t=>({id:t.id,name:`${new Date(t.created).toLocaleString()} · ${t.name}`})),state.take?.id,'尚无录音');
  settingsPanel.update(state.settings);$('recordShortcut').textContent=state.settings?.hotkeyName || '';
  showDetail();showTake();renderPackages();
}
async function play() {
  if(!state.take || !trackId){toast('请选择一条录音音轨');return;}
  audio.pause();const start=Number($('rangeStart').value),end=Number($('rangeEnd').value);
  if(end<=start || end-start>60){toast('请选择 0～60 秒的试听区间');return;}
  clipRequest=crypto.randomUUID();send('clip',{sessionId:state.take.id,groupId:trackId,start,end,gain:1,requestId:clipRequest});
}
window.chrome.webview.addEventListener('message',event=>{
  const m=event.data;
  if(m.type==='state'){state=m;render();}
  if(m.type==='error'){toast(m.message);}
  if(m.type==='activity'){
    activity=m.activity;tree.activity(activity.levels || {});$('activeCount').textContent=Object.values(activity.levels || {}).filter(p=>p>.00003).length+' 活动';
    const peak=activity.levels?.[selected] || 0;$('selectedPeak').style.width=Math.min(100,peak*100)+'%';$('peakText').textContent=peak>0?(20*Math.log10(peak)).toFixed(1)+' dB':'—';
    state.recordingPhase=m.recordingPhase;recordButtons(m.recordingPhase);
    $('recordState').textContent=m.recordingPhase==='countdown'?`倒计时 · ${Math.ceil(m.countdown)} 秒`:m.recording?`正在录制 · ${m.duration.toFixed(1)} 秒`:'录下所有独立音轨，方便回看';$('recordClock').textContent=m.recordingPhase==='countdown'?Math.ceil(m.countdown)+' 秒后开始':m.recording?m.duration.toFixed(1)+' 秒':'';
    $('feedbackState').textContent=m.feedbackDetail || '未开启';$('feedbackDot').classList.toggle('on',m.delivered);
    if(selected && activity.verified?.includes(selected))$('pcmBadge').textContent='已收到独立音轨';
  }
  if(m.type==='hotkeyLearn')settingsPanel.learned(m);
  if(m.type==='settingsSaved')toast('设置已保存');
  if(m.type==='clip' && m.requestId===clipRequest){
    if(audioUrl)URL.revokeObjectURL(audioUrl);const bytes=Uint8Array.from(atob(m.wav),c=>c.charCodeAt(0));audioUrl=URL.createObjectURL(new Blob([bytes],{type:'audio/wav'}));audio.src=audioUrl;audio.loop=$('loop').checked;audio.volume=Number($('volume').value);audio.play().catch(e=>toast('试听未能播放：'+e.message));
  }
});
$('packSelect').onchange=()=>{if(safeToLeave())send('selectPack',{id:$('packSelect').value});else $('packSelect').value=state.packId;};
$('connect').onclick=()=>send(state.connected?'disconnect':'connect');$('refresh').onclick=()=>send('refresh');$('install').onclick=()=>send('install');$('openData').onclick=()=>send('openData');
$('presetSelect').onchange=()=>{if(safeToLeave())send('loadPreset',{id:$('presetSelect').value});else $('presetSelect').value=state.preset.id;};
$('savePreset').onclick=()=>send('savePreset',{name:state.preset.name,copy:false});$('saveAs').onclick=async()=>{const name=await ask('另存为新的声音预设',state.preset?.name+' · 副本');if(name)send('savePreset',{name,copy:true});};
$('saveTrackNames').onclick=()=>send('savePreset',{name:state.preset.name,copy:false});
$('presetMore').onclick=e=>{const r=e.target.getBoundingClientRect();$('menu').style.left=Math.min(r.left,innerWidth-205)+'px';$('menu').style.top=(r.bottom+6)+'px';$('menu').hidden=!$('menu').hidden;};
document.addEventListener('click',e=>{if(!e.target.closest('#menu')&&!e.target.closest('#presetMore'))$('menu').hidden=true;});
document.querySelectorAll('[data-menu]').forEach(b=>b.onclick=async()=>{
  $('menu').hidden=true;const action=b.dataset.menu;
  if(action==='restore'&&confirm('还原初始状态？当前修改会被替换，保存后才会覆盖你的用户副本。'))send('restorePreset');
  if(action==='rename'){const name=await ask('重命名预设',state.preset.name);if(name)send('savePreset',{name,copy:false});}
  if(action==='import')send('importPreset');if(action==='export')send('exportPreset');
  if(action==='delete'&&confirm('删除当前用户预设？'))send('deletePreset',{id:state.preset.id});
});
$('addFolder').onclick=async()=>{const name=await ask('新建显示文件夹');if(name)send('addFolder',{name});};
$('applyEdit').onclick=()=>{send('editGroup',{id:selected,name:$('editName').value,folderId:$('editFolder').value,order:Number($('editOrder').value)});toast('分组显示已修改，记得保存预设');};
function filterTree(){tree.setFilter($('search').value,audibleSnapshot);}
$('search').oninput=filterTree;$('audibleOnly').onchange=()=>{audibleSnapshot=$('audibleOnly').checked?new Set(Object.entries(activity.levels || {}).filter(([,v])=>v>.00003).map(([id])=>id)):null;filterTree();};
$('refreshAudible').onclick=()=>{if($('audibleOnly').checked){audibleSnapshot=new Set(Object.entries(activity.levels || {}).filter(([,v])=>v>.00003).map(([id])=>id));filterTree();toast('已更新有声分组快照');}};$('collapse').onclick=()=>tree.collapse();
$('feedback').onchange=()=>send('feedback',{enabled:$('feedback').checked});
$('recordFromTimeline').onclick=()=>send('record',{enabled:!state.recording});
$('listenSelected').onclick=()=>{selectTrack(selected);page('recordings');};
$('takeSelect').onchange=()=>send('take',{id:$('takeSelect').value});$('trackSearch').oninput=renderTracks;$('recordAudible').onchange=renderTracks;
for(const id of ['rangeStart','rangeEnd'])$(id).onchange=()=>{timeline.range(Number($('rangeStart').value),Number($('rangeEnd').value));if(!audio.paused)play();};
$('hapticOffset').oninput=()=>{timeline.offset=Number($('hapticOffset').value);timeline.draw();};$('fullRange').onclick=()=>{if(state.take){timeline.range(0,state.take.duration);$('rangeStart').value='0';$('rangeEnd').value=state.take.duration.toFixed(2);}};
$('play').onclick=play;$('stopPlayback').onclick=()=>{audio.pause();clipRequest=null;};$('loop').onchange=()=>audio.loop=$('loop').checked;$('volume').oninput=()=>audio.volume=Number($('volume').value);
$('dismissError').onclick=()=>{$('error').hidden=true;state.error='';};
send('ready');
