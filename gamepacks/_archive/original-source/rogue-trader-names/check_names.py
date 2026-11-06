from pathlib import Path
import json

root=Path(__file__).parent
game=json.loads((root/'game.json').read_text(encoding='utf-8-sig'))
buses={int(b['id']):b for b in game['buses']}
def fnv(name):
    h=2166136261
    for b in name.lower().encode():
        h=((h*16777619)&0xffffffff)^b
    return h
words=['Master Audio Bus','Master Secondary Bus','Music','SFX','Dialogue','Dialog','Voice','Voices','VO','Ambient','Ambience','UI','GUI','Sound Effects','SoundEffects','Effects','Environment','Master','BGM','Bus_Music','Bus_SFX','Bus_Voice','Bus_Dialogue','Audio_Music','Audio_SFX','Audio_Voice','Main','Main Music','Combat','Player','Enemy','Weapons','Footsteps','Cutscene','Video','Reverb','Voiceover','Speech','MusicBus','SFXBus','DialogueBus','MusicVolume','Sound','Sounds','SFX Volume','MasterAudioBus','Cinematic','Cinematics']
matches=[{'candidate':w,'id':fnv(w),'packageName':buses[fnv(w)]['name']} for w in words if fnv(w) in buses]
report={'busCount':len(buses),'candidatesTested':len(words),'matches':matches,'note':'Hash candidates only; not original-name or semantic proof.'}
(root/'name-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
