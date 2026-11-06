from pathlib import Path
import json
import struct

work=Path(__file__).parent
managed=Path(r'E:\SteamLibrary\steamapps\common\Warhammer 40,000 Rogue Trader\WH40KRT_Data\Managed')
game=json.loads((work/'game.json').read_text(encoding='utf-8'))
buses={int(b['id']):b for b in game['buses']}
def hash_name(s):
    value=2166136261
    for byte in s.encode('utf-8'):
        if 65<=byte<=90:byte+=32
        value=((value*16777619)&0xffffffff)^byte
    return value
def u32(data,at):return struct.unpack_from('<I',data,at)[0]
def strings(data):
    base=data.find(b'BSJB')
    if base<0:return
    p=(16+u32(data,base+12)+3)&~3
    count=struct.unpack_from('<H',data,base+p+2)[0]
    p+=4
    for i in range(count):
        off,size=u32(data,base+p),u32(data,base+p+4)
        p+=8
        end=data.index(b'\0',base+p)
        name=data[base+p:end]
        p=((end-base+1)+3)&~3
        if name!=b'#US':continue
        at=base+off+1
        limit=base+off+size
        while at<limit:
            b=data[at];at+=1
            if b<128:n=b
            elif b<192:n=((b&63)<<8)|data[at];at+=1
            elif b<224:n=((b&31)<<24)|(data[at]<<16)|(data[at+1]<<8)|data[at+2];at+=3
            else:break
            if at+n>limit:break
            if 1<n<=513:
                try:yield data[at:at+n-1].decode('utf-16le')
                except UnicodeDecodeError:pass
            at+=n

matches=[]
checked=[]
for name in ['Code.dll','Kingmaker.Sound.Base.dll','AK.Wwise.Unity.API.dll','AK.Wwise.Unity.MonoBehaviour.dll','Assembly-CSharp-firstpass.dll']:
    path=managed/name
    if not path.exists():continue
    words=set(strings(path.read_bytes()))
    checked.append({'file':name,'userStrings':len(words)})
    for word in words:
        if hash_name(word) in buses:
            matches.append({'source':name,'literal':word,'id':hash_name(word),'packageName':buses[hash_name(word)]['name']})
report={'checked':checked,'matches':matches,'note':'Exact managed user-string literals hashing to current bus IDs; call-site semantics not inspected.'}
(work/'managed-string-matches.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
