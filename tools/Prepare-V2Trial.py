"""Prepare local test data from existing, already tested V1 definitions; not a game scanner."""
from pathlib import Path
import json,hashlib,zipfile,shutil
root=Path(__file__).resolve().parents[1]
sources=Path(r'E:\APEX6pro\ApexAudio\data')
out=root/'artifacts/v2-packages';out.mkdir(parents=True,exist_ok=True)
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def encode(x):return (json.dumps(x,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
def hash(b):return hashlib.sha256(b).hexdigest().upper()
backend=root/'artifacts/shared-backend'
native=backend/'ApexFmodCapture.dll';native_name='ApexFmodCapture-'+hash(native.read_bytes())[:12]+'.dll';shutil.copy2(native,backend/native_name)
backend_files={p.name:p.read_bytes() for p in backend.iterdir() if p.is_file() and p.suffix not in ['.pdb','.lib'] and p.name not in ['backend.json','ApexFmodCapture.dll'] and (not p.name.startswith('ApexFmodCapture-') or p.name==native_name)}
descriptor={'id':'apex.fmod','version':'0.2.0','entryPoint':'ApexAudio.Fmod.exe','contextVersion':1,'files':{n:hash(b) for n,b in backend_files.items()},'variants':{'studio-v1':native_name}}
backend_files['backend.json']=encode(descriptor)
def archive(path,files):
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED) as z:
  for n,b in files.items():z.writestr(n,b)
archive(out/'ApexAudio-Fmod-0.2.0.gbackend',backend_files)
for pid,label in [('local.kingdomcome2','KingdomCome2'),('apex.slay-the-spire-2','SlayTheSpire2')]:
 old=sources/'packs'/pid;game=read(old/'game.json');manifest=read(old/'manifest.json')
 oldpresets=[read(old/p) for p in manifest['presets']];users=list((sources/'presets'/pid).glob('*.json'))
 selected=read(sources/'settings.json').get('lastPresets',{}).get(pid)
 custom=next((read(p) for p in users if p.stem==selected),oldpresets[0]);groups=game['buses']
 ids={b['id']:'fmod:'+b['path'].removeprefix('bus:') for b in groups}
 for b in groups:b['stableId']=ids[b['id']]
 if pid=='apex.slay-the-spire-2':game.update(locator='studioHandle',studioHandle=game['stateOffset'],stateOffset=0)
 else:game['locator']='wrapper'
 game.pop('captureFile');game.pop('captureSha256')
 files={'profiles/current.json':encode(game),'LICENSE':(root/'LICENSE').read_bytes()}
 presets=[]
 for name,p in [('全部声音',oldpresets[0]),('动作与环境' if label=='KingdomCome2' else '音效与环境',custom)]:
  p=json.loads(json.dumps(p));p.update(id='all-sounds' if not presets else 'custom',name=name,packId=pid,basePresetId=None,basePackVersion=None)
  p['groups']={ids[k]:v for k,v in p['groups'].items() if k in ids}
  if not presets:
   for b in groups:p['groups'].setdefault(ids[b['id']],{'enabled':True})
   for g in p['groups'].values():g['enabled']=True
  path='presets/'+p['id']+'.json';files[path]=encode(p);presets.append(path)
 files['README.md']=f'# {manifest["name"]} V2 试验包\n\n仅含配置与预设，需要单独导入 ApexAudio-Fmod-0.2.0.gbackend。共享后端支持包装对象和 Studio 整数句柄两种定位，无游戏名称分支。构建摘要和关键模块精确检查仍开启。\n'.encode('utf-8')
 m={'schemaVersion':2,'id':pid,'name':manifest['name'],'version':'0.2.0','minHostVersion':'0.2.0','author':manifest['author'],'source':manifest.get('source',''),'description':'','protocolVersion':1,'demo':False,'games':[{'executable':game['executable'],'sha256':game['executableSha256'],'profileId':'current'}],
 'profiles':[{'id':'current','backend':{'id':'apex.fmod','version':'0.2.0','variant':'studio-v1','descriptorSha256':hash(backend_files['backend.json'])},'parameters':'profiles/current.json'}],'presets':presets,'files':{n:hash(b) for n,b in files.items()}}
 files['manifest.json']=encode(m);archive(out/f'ApexAudio-{label}-V2-0.2.0.gpack',files)
 print(label,'config bytes',sum(map(len,files.values())),'groups',len(groups))
(out/'SHA256SUMS.txt').write_text(''.join(hash(p.read_bytes())+'  '+p.name+'\n' for p in sorted(out.iterdir()) if p.suffix in ['.gpack','.gbackend']),encoding='utf-8')
