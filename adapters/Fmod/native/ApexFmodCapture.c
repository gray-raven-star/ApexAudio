/* FMOD 2.02 pass-through DSP taps. All game differences arrive as package data.
 * Uses FMOD's exported C API; never changes game audio volumes or output buffers. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#define API __declspec(dllexport)
#define ROUTES 64
#define FRAMES 2048
#define CAPACITY 8192
typedef struct {volatile LONG sequence;uint32_t frames,channels,config;uint64_t tick;float pcm[4096];} Block;
typedef struct {volatile LONG writes,initCalls,termCalls,invalid;uint32_t rate,config;Block blocks[64];} Stream;
typedef struct {uint32_t magic,version,pid,bankId;volatile LONG active;int32_t results[9];uint64_t frequency,started,heartbeat;Stream streams[3];} Source;
typedef struct {volatile LONG sequence;uint32_t route,frame,frames;float peak;uint32_t reserved;uint64_t tick;int16_t pcm[4096];} RecordBlock;
typedef struct {uint32_t magic,version;volatile LONG active,writes;uint32_t frame,limit,capacity,blockSize;uint64_t heartbeat,frequency;uint32_t reserved[4];RecordBlock blocks[CAPACITY];} Recording;
_Static_assert(sizeof(Source)==3150488,"source ABI");
_Static_assert(sizeof(RecordBlock)==8224,"record ABI");
typedef struct {void*instance,*plugindata;uint32_t channelmask;int speakermode;float*sidechain;int sidechainchannels;void*functions;int systemobject;} DspState;
typedef int(*Read)(DspState*,float*,float*,unsigned,int,int*);
typedef struct {uint32_t sdk;char name[32];uint32_t version;int inputs,outputs;void*create,*release,*reset;Read read;void*process,*setposition;int numparameters;void*paramdesc;void*parametercallbacks[8];void*shouldprocess,*userdata;void*systemcallbacks[3];} Description;
typedef struct {uint32_t magic,version,wrapperRva,runtimeVersion,count,stateOffset,studioOffset;WCHAR wrapperModule[128];unsigned char ids[ROUTES][16];} Configuration;
typedef struct {void*bus,*group,*dsp;volatile LONG selected,touched;uint32_t frames;float pcm[4096];int index;} Route;
static Configuration cfg;static Source*source;static Recording*recording;static HANDLE sourceMap,recordMap;
static Route routes[ROUTES];static void*studio,*core,*masterDsp,*masterGroup;
static int(*studioValid)(void*),(*getCore)(void*,void**),(*getVersion)(void*,unsigned*),(*getFormat)(void*,int*,int*,int*);
static int(*getBus)(void*,const void*,void**),(*busLock)(void*),(*busUnlock)(void*),(*busGroup)(void*,void**);
static int(*createDsp)(void*,const Description*,void**),(*dspUser)(void*,void*),(*getDspUser)(void*,void**);
static int(*addDsp)(void*,int,void*),(*removeDsp)(void*,void*),(*releaseDsp)(void*),(*lockDsp)(void*),(*unlockDsp)(void*),(*getMaster)(void*,void**);
static uint64_t now(void){LARGE_INTEGER q;QueryPerformanceCounter(&q);return q.QuadPart;}
static void name(WCHAR*out,const WCHAR*prefix){lstrcpyW(out,prefix);unsigned p=GetCurrentProcessId(),n=0,at=lstrlenW(out);WCHAR digits[16];do{digits[n++]=L'0'+p%10;p/=10;}while(p);while(n)out[at++]=digits[--n];out[at]=0;}
static int16_t pcm16(float value){if(!(value==value))value=0;if(value>1)value=1;if(value< -1)value=-1;return (int16_t)(value*32767);}
static void stereo(Route*r,float*input,unsigned length,int channels){
 r->frames=length;for(unsigned i=0;i<length;i++){
  float*l=input+i*channels;float a=l[0],b=channels>1?l[1]:a;
  if(channels>2){a+=l[2]*.70710678f;b+=l[2]*.70710678f;}
  if(channels>3){a+=l[3]*.5f;b+=l[3]*.5f;}
  for(int c=4;c<channels;c++){if(c&1)b+=l[c]*.5f;else a+=l[c]*.5f;}
  r->pcm[i]=a;r->pcm[FRAMES+i]=b;
 }
}
static int tap(DspState*state,float*input,float*output,unsigned length,int channels,int*outChannels){
 if(!input||!output||channels<1)return 0;
 *outChannels=channels;if(input!=output)memcpy(output,input,length*channels*sizeof(float));
 if(!source||!source->active||length>FRAMES||channels>16)return 0;
 uint64_t tick=now(),heartbeat=source->heartbeat;if(tick>heartbeat&&tick-heartbeat>source->frequency*10){source->active=0;recording->active=0;return 0;}
 Route*r=NULL;if(getDspUser(state->instance,(void**)&r)||!r)return 0;
 if(r->index<0){
  float mix[4096]={0};for(unsigned g=0;g<cfg.count;g++){
   Route*route=&routes[g];if(InterlockedExchange(&route->touched,0)&&route->selected){unsigned count=route->frames<length?route->frames:length;for(unsigned i=0;i<count;i++){mix[i]+=route->pcm[i];mix[FRAMES+i]+=route->pcm[FRAMES+i];}}
  }
  Stream*s=&source->streams[0];Block*b=&s->blocks[(unsigned)s->writes%64];InterlockedIncrement(&b->sequence);
  b->frames=length;b->channels=2;b->config=0x3102;b->tick=tick-(uint64_t)length*source->frequency/48000;
  memcpy(b->pcm,mix,length*sizeof(float));memcpy(b->pcm+FRAMES,mix+FRAMES,length*sizeof(float));
  InterlockedIncrement(&b->sequence);InterlockedIncrement(&s->writes);recording->frame+=length;
 }else{
  stereo(r,input,length,channels);InterlockedExchange(&r->touched,1);
  if(recording->active){
   float peak=0;for(unsigned i=0;i<length;i++){float a=r->pcm[i],b=r->pcm[FRAMES+i];if(a<0)a=-a;if(b<0)b=-b;if(a>peak)peak=a;if(b>peak)peak=b;}
   if(peak>.00003f){RecordBlock*b=&recording->blocks[(unsigned)recording->writes%CAPACITY];InterlockedIncrement(&b->sequence);
    b->route=r->index;b->frame=recording->frame;b->frames=length;b->peak=peak;b->tick=tick-(uint64_t)length*source->frequency/48000;
    for(unsigned i=0;i<length;i++){b->pcm[i*2]=pcm16(r->pcm[i]);b->pcm[i*2+1]=pcm16(r->pcm[FRAMES+i]);}
    InterlockedIncrement(&b->sequence);InterlockedIncrement(&recording->writes);
   }
  }
 }
 return 0;
}
static Route masterRoute={.index=-1};
static int attach(void*group,Route*r,void**dsp){Description d={0};d.sdk=110;memcpy(d.name,"Apex pass-through tap",22);d.version=0x10000;d.inputs=d.outputs=1;d.read=tap;int e=createDsp(core,&d,dsp);if(e)return e;e=dspUser(*dsp,r);if(e)return e;return addDsp(group,-1,*dsp);}
API DWORD FmodStop(void*unused){
 (void)unused;if(!core)return 0;lockDsp(core);if(source)source->active=0;if(recording)recording->active=0;
 if(masterDsp){removeDsp(masterGroup,masterDsp);releaseDsp(masterDsp);masterDsp=NULL;}
 for(unsigned i=0;i<cfg.count;i++){Route*r=&routes[i];if(r->dsp){removeDsp(r->group,r->dsp);releaseDsp(r->dsp);r->dsp=NULL;}if(r->bus){busUnlock(r->bus);r->bus=NULL;}}
 unlockDsp(core);return 0;
}
API DWORD FmodConfigure(void*configuration){if(source&&source->active)return 100;Configuration*c=configuration;if(!c||c->magic!=0x314d4641||c->version!=2||c->count<1||c->count>ROUTES||c->wrapperModule[127]||c->stateOffset>4092||c->studioOffset>4088)return 1;cfg=*c;return 0;}
#define LOAD(module,variable,entry) variable=(void*)GetProcAddress(module,entry);if(!variable)return 2
API DWORD FmodStart(void*unused){
 (void)unused;if(source&&source->active)return 100;
 HMODULE s=GetModuleHandleW(L"fmodstudio.dll"),c=GetModuleHandleW(L"fmod.dll"),wrapper=GetModuleHandleW(cfg.wrapperModule);if(!s||!c||!wrapper)return 1;
 LOAD(s,studioValid,"FMOD_Studio_System_IsValid");LOAD(s,getCore,"FMOD_Studio_System_GetCoreSystem");LOAD(c,getVersion,"FMOD_System_GetVersion");LOAD(c,getFormat,"FMOD_System_GetSoftwareFormat");
 LOAD(s,getBus,"FMOD_Studio_System_GetBusByID");LOAD(s,busLock,"FMOD_Studio_Bus_LockChannelGroup");LOAD(s,busUnlock,"FMOD_Studio_Bus_UnlockChannelGroup");LOAD(s,busGroup,"FMOD_Studio_Bus_GetChannelGroup");
 LOAD(c,createDsp,"FMOD_System_CreateDSP");LOAD(c,dspUser,"FMOD_DSP_SetUserData");LOAD(c,getDspUser,"FMOD_DSP_GetUserData");LOAD(c,addDsp,"FMOD_ChannelGroup_AddDSP");LOAD(c,removeDsp,"FMOD_ChannelGroup_RemoveDSP");LOAD(c,releaseDsp,"FMOD_DSP_Release");
 LOAD(c,lockDsp,"FMOD_System_LockDSP");LOAD(c,unlockDsp,"FMOD_System_UnlockDSP");LOAD(c,getMaster,"FMOD_System_GetMasterChannelGroup");
 unsigned char*object=*(void**)((unsigned char*)wrapper+cfg.wrapperRva);if(!object||*(unsigned*)(object+cfg.stateOffset)!=2)return 3;
 studio=*(void**)(object+cfg.studioOffset);if(!studioValid(studio)||getCore(studio,&core))return 3;
 unsigned version=0;if(getVersion(core,&version)||version!=cfg.runtimeVersion)return 4;int rate=0;if(getFormat(core,&rate,NULL,NULL)||rate!=48000)return 5;
 WCHAR mapping[64];if(!source){name(mapping,L"Local\\ApexFmodMix-");sourceMap=CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,sizeof(Source),mapping);if(sourceMap)source=MapViewOfFile(sourceMap,FILE_MAP_ALL_ACCESS,0,0,sizeof(Source));
  if(source&&source->magic==0x37504341&&source->active){UnmapViewOfFile(source);CloseHandle(sourceMap);source=NULL;sourceMap=NULL;return 108;}}
 if(!recording){name(mapping,L"Local\\ApexFmodTracks-");recordMap=CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,sizeof(Recording),mapping);if(recordMap)recording=MapViewOfFile(recordMap,FILE_MAP_ALL_ACCESS,0,0,sizeof(Recording));}
 if(!source||!recording)return 6;memset(source,0,sizeof(Source));memset(recording,0,sizeof(Recording));memset(routes,0,sizeof(routes));
 LARGE_INTEGER frequency;QueryPerformanceFrequency(&frequency);source->magic=0x37504341;source->version=1;source->pid=GetCurrentProcessId();source->frequency=frequency.QuadPart;source->heartbeat=source->started=now();source->streams[0].rate=48000;source->streams[0].config=0x3102;
 recording->magic=0x31544641;recording->version=1;recording->capacity=CAPACITY;recording->blockSize=sizeof(RecordBlock);recording->frequency=frequency.QuadPart;
 // LockChannelGroup ensures even silent buses have a stable tap point.
 for(unsigned i=0;i<cfg.count;i++){Route*r=&routes[i];r->index=i;if(getBus(studio,cfg.ids[i],&r->bus)||busLock(r->bus)){FmodStop(NULL);return 7;}}
 int(*flush)(void*)=(void*)GetProcAddress(s,"FMOD_Studio_System_FlushCommands");if(!flush||flush(studio)){FmodStop(NULL);return 8;}
 lockDsp(core);int error=getMaster(core,&masterGroup);
 for(unsigned i=0;i<cfg.count&&!error;i++){Route*r=&routes[i];error=busGroup(r->bus,&r->group);if(!error)error=attach(r->group,r,&r->dsp);}
 if(!error)error=attach(masterGroup,&masterRoute,&masterDsp);
 if(!error){recording->active=1;source->active=1;}unlockDsp(core);
 if(error){FmodStop(NULL);return 1000+error;}return 0;
}
API DWORD FmodSelect(void*mask){if(!mask)return 1;uint32_t*selected=mask;for(unsigned i=0;i<cfg.count;i++)InterlockedExchange(&routes[i].selected,selected[i]?1:0);return 0;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,void*reserved){(void)reserved;if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(instance);return TRUE;}
