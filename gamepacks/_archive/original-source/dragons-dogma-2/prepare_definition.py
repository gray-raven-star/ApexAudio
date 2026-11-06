from pathlib import Path
import json,base64,struct,hashlib
r=Path(__file__).parent;d=r/'definition';(d/'presets').mkdir(parents=True,exist_ok=True)
rows=json.loads((r/'named-buses.json').read_text(encoding='utf-8'));byid={b['id']:b for b in rows};rawnames={b['id']:b['names'][0] if b['names'] else '' for b in rows}
translations={
'Master Audio Bus':'总混音（包含音乐和对白）','Master Secondary Bus':'次级总混音','gm_Master':'场景机关与物件总线','gm_se':'机关与物件音效','gm_sp':'机关与物件特殊音效',
'em_Master':'敌人总线（包含叫声）','em_se_obj':'敌人物件音效','em_vo_obj':'敌人物件发声','em_fx_obj':'敌人物件效果','em_vo_dia':'敌人对话','em_lfe':'敌人低频混音','em_vo_roars':'敌人吼叫','em_link':'敌人联动混音','em_se_link':'敌人联动音效','em_vo_link':'敌人联动发声','em_se':'敌人动作音效','em_vo':'敌人发声','em_fx':'敌人效果音','fx_imp':'敌人效果冲击','fx_pre':'敌人效果准备段','em_whoosh_sp':'敌人特殊挥动声','em_se_imp':'敌人冲击音效',
'skill_Master':'技能总线','skill_mgc':'魔法技能','skill_se':'技能音效','Master_bgm':'背景音乐总线','cutscene_ovw_bgm':'剧情演出音乐','playable_bgm':'游玩音乐','crime_bgm':'犯罪事件音乐','crime_crime':'犯罪状态音乐','crime_arrested':'被捕音乐','ovw_bgm':'剧情相关音乐','ovw_normal_bgm':'常规剧情音乐','battle_bgm':'战斗音乐','battle_normal_bgm':'普通战斗音乐','battle_ovw_bgm':'剧情战斗音乐','battle_boss_bgm':'首领战音乐','battle_adv_bgm':'优势战斗音乐','field_ride_bgm':'骑乘与移动音乐','field_indoor_ride_bgm':'室内骑乘音乐','field_field_ride_bgm':'野外骑乘音乐','field_town_ride_bgm':'城镇骑乘音乐','field_otherworld_ride_bgm':'异界骑乘音乐','send_reverb':'音乐混响发送','musician_bgm':'演奏者音乐','dungeon_bgm':'地牢音乐','field_bgm':'探索音乐','field_field_bgm':'野外探索音乐','field_town_poss_bgm':'城镇位置音乐','field_otherworld_bgm':'异界探索音乐','field_indoor_bgm':'室内探索音乐','field_town_bgm':'城镇探索音乐','system_bgm':'系统音乐','cutscene_bgm':'过场音乐',
'Master_cutscene':'过场总线（含对白）','cutscene_Master':'过场混音（含对白）','cutscene_dialog':'过场对白','cutscene_se':'过场音效','charamake_pw_dialog':'随从捏脸对白','charamake_human_dialog':'人物捏脸对白','Master_dialog':'对白总线','pw_dialog':'随从对白','human_dialog':'人物对白','enm_dialog':'敌人对白',
'Master_se':'音效总混音（含人物发声）','quest_master':'任务总线','quest_se':'任务音效','menu_ui':'菜单与界面','npc30_a_veryhigh_ssl':'NPC音轨（编号30）','npc_wear_foot_es_settlement':'聚落NPC衣物与脚步',
'env_Master':'环境总线（含生物与人群）','env_table':'环境混音','env_terrain':'地形与场景环境','env_terrain_base':'基础场景环境','env_terrain_pos':'空间场景环境','env_terrain_pos_water':'空间水声','env_terrain_pos_rnd':'随机空间环境声','env_terrain_pos_com':'常规空间环境声','env_terrain_pos_blend':'空间环境混合','env_terrain_pos_weather':'空间天气声','env_terrain_painter':'场景布置环境声','env_terrain_painter_tree':'树木环境声','env_terrain_painter_com':'通用布置环境声','env_terrain_painter_glass':'草木环境声（原名glass）','env_terrain_painter_water':'场景布置水声','env_critters':'小型生物环境声','env_critters_base':'基础小型生物声','env_critters_painter':'场景布置生物声','env_critters_pos':'空间生物声','env_gaya':'环境人群声（语义待试听）','env_weather':'天气环境声','env_otherworld':'异界环境声','env_backworld':'背面世界环境声（原名backworld）','env_backworld_weather':'背面世界天气','env_backworld_gimmick':'背面世界机关','env_ev':'环境事件声','env_gimmick_com':'通用环境机关','env_em_battle':'敌人战斗环境声','env_epv':'环境EPV音轨（用途待确认）',
'dmg_Master':'受伤与状态总线','dmg_se':'受伤音效','condition':'状态音效','hm_Master':'人形角色总线（含语音）','pw_Master':'随从总线（含语音）','pw_se':'随从动作音效','pw_vo':'随从发声','npc_Master':'NPC总线（含语音）','npc_se':'NPC动作音效','npc_vo':'NPC发声','pl_Master':'玩家总线（含发声）','pl_vo':'玩家发声','pl_se':'玩家动作音效','wear_Master':'衣物与脚步混音','wear_npc':'NPC衣物与脚步','wear_pl':'玩家衣物与脚步','wear_pw':'随从衣物与脚步','wep_master':'武器总线','wep_imp':'武器碰撞与命中','wep_foley':'武器操作与摩擦','wep_whs':'武器挥动','reverb_Master':'共享混响（可能含对白）','special':'特殊混响','se_send_reverb':'音效混响发送','dialogue':'对白混响','item_Master':'道具总线','item':'道具音效','ingame_controller_sfx':'游戏手柄音效通道'}
def lineage(b):
 chain=[];seen=set();cur=b
 while cur and cur['id'] not in seen:
  seen.add(cur['id']);chain.append(rawnames[cur['id']]);cur=byid.get(cur['parent'])
 return chain

def category(b):
 ns=lineage(b)
 if 'Master_bgm' in ns:return '音乐'
 if any(n in ('Master_dialog','cutscene_dialog','charamake_pw_dialog','charamake_human_dialog','dialogue','em_vo_dia') or n.endswith('_vo') or n.startswith('em_vo_') for n in ns):return '对白与发声'
 if 'reverb_Master' in ns or b['auxiliary']:return '共享混响'
 if 'env_Master' in ns:return '环境'
 if 'em_Master' in ns:return '敌人'
 if any(n in ns for n in ['pl_Master','pw_Master','npc_Master','hm_Master']):return '人物动作'
 if any(n in ns for n in ['wep_master','skill_Master']):return '武器与技能'
 if 'wear_Master' in ns:return '衣物与脚步'
 if 'Master_cutscene' in ns:return '过场'
 if 'item_Master' in ns:return '道具'
 if 'dmg_Master' in ns:return '受伤与状态'
 if any(n in ns for n in ['gm_Master','quest_master','menu_ui']):return '机关与界面'
 return '混合与未确认'

evidence=[];buses=[]
for b in rows:
 raw=rawnames[b['id']];cat=category(b)
 if raw:
  zh=translations.get(raw)
  label=(zh+' · '+raw) if zh else raw+'（原名，含义待确认）'
  source='公开目标游戏名称表与当前Bus ID哈希匹配；中文为解释，非游戏原始中文名'
 else:
  label=f'未命名{cat}分组 · {b["id"]}';source='稳定编号；分类仅由父级路由推导'
 buses.append(dict(id=b['id'],parent=b['parent'],name=label,nameSource=source,category=cat,auxiliary=b['auxiliary'],slot=b['slot']))
 evidence.append({'id':str(b['id']),'originalName':raw or None,'nameCandidates':b['names'],'effects':{'selectedSlot':b['slot'],'occupiedSlots':b['occupiedSlots'],'verification':'static-only','source':'group-routing-evidence.json','sourceSha256':hashlib.sha256((r/'buses.json').read_bytes()).hexdigest().upper(),'targetSha256':'53D0FA7093CA1B560E0C881C84A6451091F87D3AB678D25F3EFFF08C22128127'}})
# Keep original private bank and reference list outside the distributable package.
(d/'group-routing-evidence.json').write_bytes((r/'buses.json').read_bytes())
bank=(r/'metadata/Init.bnk').read_bytes();hdr=bank[8:8+struct.unpack_from('<I',bank,4)[0]];digest='53D0FA7093CA1B560E0C881C84A6451091F87D3AB678D25F3EFFF08C22128127';pid='local.dragonsdogma2'
game=dict(title='龙之信条 2',gameSha256=digest,engineModule='DD2.exe',engineSha256=digest,bankVersion=140,bankHeader=base64.b64encode(hdr).decode(),bankId=0x7a0c0224,captureFile='modules/ApexDD2Capture.dll',captureSha256='',sdkYear=2021,sdkSeries=1,buses=buses)
selected_names={'gm_Master','em_se','em_fx','em_se_imp','em_whoosh_sp','em_se_link','skill_Master','quest_se','menu_ui','dmg_Master','pw_se','npc_se','pl_se','wear_Master','wep_master','item_Master'}
env_names={'env_terrain','env_weather','env_otherworld','env_backworld_weather','env_backworld_gimmick','env_gimmick_com','env_em_battle'}
player_names={'pl_se','wear_pl','wep_master','dmg_se','item','menu_ui'}
manifest=dict(schemaVersion=1,id=pid,name='龙之信条 2',version='0.1.0',author='Apex Audio',source='',description='',protocolVersion=1,entryPoint='ApexAudio.Wwise.exe',demo=False,games=[dict(executable='DD2.exe',sha256=digest)],presets=['presets/action-environment.json','presets/action-only.json','presets/player-weapons.json'],files={})
for key,title,chosen in [('action-environment','动作与环境',selected_names|env_names),('action-only','动作音效',selected_names),('player-weapons','玩家与武器',player_names)]:
 selected={b['id'] for b in rows if rawnames[b['id']] in chosen and not b['auxiliary']}
 # A mix must not select a bus and one of its ancestors simultaneously.
 for id in selected:
  cur=byid[id]['parent'];seen=set()
  while cur and cur not in seen:
   assert cur not in selected;seen.add(cur);cur=byid[cur]['parent'] if cur in byid else 0
 preset=dict(schemaVersion=1,id=key,name=title,packId=pid,folders=[],groups={str(b['id']):dict(name=b['name'],enabled=b['id'] in selected,order=i) for i,b in enumerate(buses) if not b['auxiliary']})
 (d/'presets'/f'{key}.json').write_text(json.dumps(preset,ensure_ascii=False,indent=2),encoding='utf-8')
(d/'game.json').write_text(json.dumps(game,ensure_ascii=False,indent=2),encoding='utf-8');(d/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
(d/'group-evidence.json').write_text(json.dumps({'schemaVersion':1,'packId':pid,'packVersion':'0.1.0','bankSha256':hashlib.sha256(bank).hexdigest(),'parser':'wwiser v20260808','nameReference':"https://github.com/bnnm/wwiser-utils/blob/master/wwnames/Dragon%27s%20Dogma%20II%20(PC).txt",'nameReferenceSha256':hashlib.sha256((r/'sources/dd2-wwnames.txt').read_bytes()).hexdigest(),'groups':evidence},ensure_ascii=False,indent=2),encoding='utf-8')
(d/'README.md').write_text('''# 龙之信条 2

版本 0.1.0。适用于当前 DD2.exe 文件版本 4.1.0.0、SHA256 53D0FA7093CA1B560E0C881C84A6451091F87D3AB678D25F3EFFF08C22128127。

在 ApexAudio 的游戏适配包页面导入 .gpack，首个方案为动作与环境。另有动作音效、玩家与武器两个方案。音乐、对白、普通人物发声与共享混响默认不加入补充震动，游戏正常声音不变。敌人动作保留在前两个方案；玩家与武器不主动选择敌人路由。技能与共享武器效果可能仍服务多种角色，不能承诺完全按角色隔离。

210 条普通总线可采集，另有67条辅助混响元数据。129条普通总线的名字与公开目标游戏名称表匹配，中文为解释；其余保留当前编号，分类依据真实父级。匹配不保证名称唯一或精确声音语义，缩写未确认时保留原文。完整依据见group-evidence.json。

父组已混入子组的声音，不能靠关闭子组从父组中扣除。本包默认选择互不重叠的路由，不选角色或音效大总混音，也不把共享混响当作纯音效。具体细分效果可在工作台试听后修改。

原始银行、媒体、游戏全量资源和公开整份名称库不包含在包中。主程序不修改，仅有包内独立采集模块。初次实际采集验证尚未完成。
''',encoding='utf-8')
print('Definition ready: 210 PCM groups, 67 auxiliary records, 129 named ordinary groups.')
