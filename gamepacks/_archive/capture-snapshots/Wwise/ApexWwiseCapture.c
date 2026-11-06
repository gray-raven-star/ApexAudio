/* Version-selected pass-through Wwise capture effect. Copies PCM only.
 * No game file edits, executable patches, volume changes or buffer mutations.
 * The Windows x64 MSVC interface is expressed as C vtables, avoiding GNU C++ ABI.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <stddef.h>

#define API __declspec(dllexport)
#define STREAMS 3
#define ROUTES 1024
#define BLOCKS 64
#define FRAMES 2048
#define CHANNELS 2
#ifndef CAPTURE_BANK_VERSION
#define CAPTURE_BANK_VERSION 0
#endif
#ifndef CAPTURE_SDK_YEAR
#define CAPTURE_SDK_YEAR 0
#endif
#ifndef CAPTURE_SDK_SERIES
#define CAPTURE_SDK_SERIES 1
#endif
typedef struct {float *data;uint32_t config;int32_t state;uint16_t maxFrames,validFrames;} AudioBuffer;
typedef struct {volatile LONG sequence;uint32_t frames,channels,config;uint64_t tick;float pcm[FRAMES*CHANNELS];} Block;
typedef struct {volatile LONG writes,initCalls,termCalls,invalid;uint32_t rate,config;Block blocks[BLOCKS];} Stream;
typedef struct {uint32_t magic,version,pid,bankId;volatile LONG active;int32_t registerResult,bankResult,attachResults[STREAMS],detachResults[STREAMS];uint64_t frequency,started,heartbeat;Stream streams[STREAMS];} Shared;
typedef struct {void **vtable;uint32_t stream;} Params;
typedef struct {void **vtable;uint32_t stream;} Effect;
static Shared *shared;
static HANDLE mapping;
static int registered,loaded,callbackRegistered;
typedef struct { volatile LONG touched,calls; uint32_t rate,config,frames; float pcm[FRAMES*2]; } RouteBuffer;
static RouteBuffer routeBuffers[ROUTES];
static int routeAttach[ROUTES];
typedef void (*GlobalCallback)(void*,uint32_t,void*);
static int (*registerCallback)(GlobalCallback,uint32_t,void*,int,uint32_t,uint32_t);
static int (*unregisterCallback)(GlobalCallback,uint32_t);
static DWORD callbackResult;
static volatile LONG options=7;

typedef struct {uint32_t magic,version,bankVersion,year,count,reserved,headerSize,bankId;uint32_t ids[ROUTES],slots[ROUTES],includes[ROUTES];unsigned char header[64];WCHAR module[128];} Config;
typedef struct {uint32_t id,slot;volatile LONG enabled;int32_t attach;float peak,rms;uint32_t calls,invalid;} Group;
typedef struct {uint32_t magic,version,pid,count;volatile LONG active;int32_t callbackResult;volatile LONG published,monitor;uint64_t generation;Group groups[ROUTES];} Grid;
static Config cfg;static int configured;static uint32_t renderFrames=1024;
static uint32_t busIds[ROUTES],fxIds[ROUTES];
static unsigned char *captureBank;static uint32_t captureBankSize;
static Grid *grid;static HANDLE gridMapping;
/* Recording has its own bounded ring; feedback and single-bus monitoring retain
 * their existing ABI. No allocation or disk IO is performed by audio callbacks. */
#define REC_BLOCKS 8192
typedef struct {volatile LONG sequence;uint32_t route,frame,frames;float peak;uint32_t reserved[3];int16_t pcm[FRAMES*2];} RecordBlock;
typedef struct {uint32_t magic,version;volatile LONG state,writes;uint32_t frame,limit,capacity,blockSize;uint64_t heartbeat,frequency;uint32_t reserved[4];RecordBlock blocks[REC_BLOCKS];} Recording;
_Static_assert(sizeof(RecordBlock)==8224,"record block layout");
_Static_assert(offsetof(Recording,blocks)==64,"record header layout");
static Recording *recording;static HANDLE recordingMapping;
static int16_t pcm16(float v){if(!(v==v))return 0;if(v>1)v=1;if(v< -1)v=-1;return (int16_t)(v*32767);}
static void globalRender(void*,uint32_t,void*);
static int (*setBusEffect)(uint32_t,uint32_t,uint32_t);
static void **paramTable,**effectTable;
/* 2016.2 has no PostMessagesProcessed callback: render locations are one bit
 * earlier. Using modern 16/32 clears the PCM at the legacy EndRender callback. */
static uint32_t beginRenderLocation(void){return cfg.year<=2016?8u:16u;}
static uint32_t endRenderLocation(void){return cfg.year<=2016?16u:32u;}
static uint32_t renderLocations(void){return beginRenderLocation()|endRenderLocation();}
static uint64_t now(void){LARGE_INTEGER q;QueryPerformanceCounter(&q);return q.QuadPart;}
static void *allocate(size_t n){return HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,n);}
static void release(void*p){if(p)HeapFree(GetProcessHeap(),0,p);}

static void *paramDelete(Params*self,unsigned flags){if(flags&1)release(self);return self;}
static int paramSet(Params*self,uint16_t id,const void*data,uint32_t size){(void)self;(void)id;(void)data;(void)size;return 1;}
static Params *paramClone(Params*self,void*allocator){(void)allocator;Params*p=allocate(sizeof(*p));if(p)*p=*self;return p;}
static int paramInit(Params*self,void*allocator,const void*data,uint32_t size){(void)allocator;if(size!=4||!data)return 31;memcpy(&self->stream,data,4);return self->stream<ROUTES?1:31;}
static int paramTerm(Params*self,void*allocator){(void)allocator;release(self);return 1;}
static int paramBlock(Params*self,const void*data,uint32_t size){return paramInit(self,NULL,data,size);}
static Params *createParams(void*allocator){(void)allocator;Params*p=allocate(sizeof(*p));if(p)p->vtable=paramTable;return p;}

static void *effectDelete(Effect*self,unsigned flags){if(flags&1)release(self);return self;}
static int effectTerm(Effect*self,void*allocator){(void)allocator;release(self);return 1;}
static int effectReset(Effect*self){(void)self;return 1;}
static int effectInfo(Effect*self,void*info){(void)self;unsigned char*out=info;memset(out,0,12);uint32_t type=3,build=(cfg.year<<8)|(cfg.reserved?cfg.reserved:1u);memcpy(out,&type,4);memcpy(out+4,&build,4);out[8]=1;return 1;}
static _Bool effectRelocation(Effect*self){(void)self;return 0;}
static int effectRelocate(Effect*self,void*a,void*b){(void)self;(void)a;(void)b;return 0;}
static int effectInit(Effect*self,void*allocator,void*context,Params*params,void*format){
 (void)allocator;(void)context;if(!params||params->stream>=cfg.count)return 31;self->stream=params->stream;
 RouteBuffer*r=&routeBuffers[self->stream];memcpy(&r->rate,format,4);memcpy(&r->config,(unsigned char*)format+4,4);if(shared)InterlockedIncrement(&shared->streams[0].initCalls);return 1;
}
static void publish(unsigned stream, const float *pcm, unsigned frames, uint64_t tick) {
 Stream*s=&shared->streams[stream];Block*b=&s->blocks[(unsigned)s->writes%BLOCKS];
 InterlockedIncrement(&b->sequence);b->frames=frames;b->channels=2;b->config=0x3102;b->tick=tick;
 memcpy(b->pcm,pcm,frames*sizeof(float));memcpy(b->pcm+FRAMES,pcm+FRAMES,frames*sizeof(float));
 InterlockedIncrement(&b->sequence);InterlockedIncrement(&s->writes);
}
static void effectExecute(Effect*self,AudioBuffer*buffer) {
 if(!shared||!shared->active||self->stream>=cfg.count||!buffer||!buffer->data)return;
 unsigned index=0;
 Stream*s=&shared->streams[index];unsigned ch=buffer->config&255u;
 if(!ch||ch>32||buffer->maxFrames>FRAMES||buffer->validFrames>buffer->maxFrames){InterlockedIncrement(&s->invalid);if(grid)InterlockedIncrement((volatile LONG*)&grid->groups[self->stream].invalid);return;}
 uint64_t heartbeat=shared->heartbeat; if(!buffer->validFrames||now()-heartbeat>shared->frequency*10)return;
 RouteBuffer*r=&routeBuffers[self->stream];r->frames=buffer->validFrames;
 memset(r->pcm,0,r->frames*sizeof(float));memset(r->pcm+FRAMES,0,r->frames*sizeof(float));
 uint32_t mask=buffer->config>>12,remaining=mask&~8u;unsigned standard=(buffer->config>>8)&15u;
 for(unsigned c=0;c<ch;c++){
  float left=0,right=0;uint32_t speaker=0;
  if(standard==1){if((mask&8u)&&c==ch-1)speaker=8u;else {speaker=remaining&(~remaining+1u);remaining&=~speaker;}}
  if(ch==1){left=right=1;}
  else if(ch==2){left=c==0;right=c==1;}
  else if(speaker==1){left=1;}
  else if(speaker==2){right=1;}
  else if(speaker==4){left=right=.70710678f;}
  else if(speaker==8){left=right=.5f;}
  else if(speaker==0x10||speaker==0x40||speaker==0x200||speaker==0x1000||speaker==0x8000||speaker==0x40000){left=.5f;}
  else if(speaker==0x20||speaker==0x80||speaker==0x400||speaker==0x4000||speaker==0x20000||speaker==0x100000){right=.5f;}
  else {left=right=.35355339f;}
  const float*src=buffer->data+c*buffer->maxFrames;
  for(unsigned f=0;f<r->frames;f++){r->pcm[f]+=src[f]*left;r->pcm[FRAMES+f]+=src[f]*right;}
 }
 InterlockedIncrement(&r->calls);InterlockedExchange(&r->touched,1);
}
static void globalRender(void*context,uint32_t location,void*cookie) {
 (void)context;(void)cookie;if(!shared||!shared->active||!grid)return;
 if(location==beginRenderLocation()){for(unsigned i=0;i<cfg.count;i++)InterlockedExchange(&routeBuffers[i].touched,0);return;}
 if(location!=endRenderLocation())return;
 uint64_t heartbeat=shared->heartbeat; if(now()-heartbeat>shared->frequency*10){shared->active=0;grid->active=0;return;}
 unsigned frames=renderFrames;float pcm[FRAMES*2]={0};
 for(unsigned i=0;i<cfg.count;i++){
  RouteBuffer*r=&routeBuffers[i];Group*g=&grid->groups[i];float peak=0,sum=0;
  if(r->touched){for(unsigned f=0;f<r->frames;f++){float l=r->pcm[f],rr=r->pcm[FRAMES+f],al=l<0?-l:l,ar=rr<0?-rr:rr;if(al>peak)peak=al;if(ar>peak)peak=ar;sum+=l*l+rr*rr;}
   if(InterlockedCompareExchange(&g->enabled,0,0)){if(r->frames>frames)frames=r->frames;for(unsigned f=0;f<r->frames;f++){pcm[f]+=r->pcm[f];pcm[FRAMES+f]+=r->pcm[FRAMES+f];}}
  }
  g->peak=peak;g->rms=r->touched?(float)__builtin_sqrtf(sum/(r->frames*2)):0;g->calls=r->calls;
 }
 uint64_t tick=now();publish(0,pcm,frames,tick);
 if(recording){
  LONG state=InterlockedCompareExchange(&recording->state,0,0);
  if(state==1){recording->frame=0;InterlockedExchange(&recording->state,2);state=2;}
  if(state==2){
   uint64_t heartbeat=recording->heartbeat;
   if(tick>heartbeat && tick-heartbeat>shared->frequency*3){InterlockedExchange(&recording->state,3);}
   else {
    unsigned at=recording->frame,remaining=recording->limit>at?recording->limit-at:0;
    for(unsigned i=0;i<cfg.count&&remaining;i++){
     RouteBuffer*r=&routeBuffers[i];if(!r->touched||grid->groups[i].peak<=.00003f)continue;
     unsigned n=r->frames<remaining?r->frames:remaining;RecordBlock*b=&recording->blocks[(unsigned)recording->writes%REC_BLOCKS];
     InterlockedIncrement(&b->sequence);b->route=i;b->frame=at;b->frames=n;b->peak=grid->groups[i].peak;
     for(unsigned f=0;f<n;f++){b->pcm[f*2]=pcm16(r->pcm[f]);b->pcm[f*2+1]=pcm16(r->pcm[FRAMES+f]);}
     InterlockedIncrement(&b->sequence);InterlockedIncrement(&recording->writes);
    }
    recording->frame+=frames<remaining?frames:remaining;
    if(recording->frame>=recording->limit)InterlockedExchange(&recording->state,3);
   }
  }
 }
 LONG monitor=InterlockedCompareExchange(&grid->monitor,0,0);
 if(monitor>=0&&(unsigned)monitor<cfg.count&&routeBuffers[monitor].touched)publish(1,routeBuffers[monitor].pcm,routeBuffers[monitor].frames,tick);
 InterlockedIncrement(&grid->published);
}
static int effectSkip(Effect*self,uint32_t frames){(void)self;(void)frames;return cfg.year<=2016?45:43;}
static Effect *createEffect(void*allocator){(void)allocator;Effect*p=allocate(sizeof(*p));if(p)p->vtable=effectTable;return p;}
static void *paramMethods[]={paramDelete,paramSet,paramClone,paramInit,paramTerm,paramBlock};
static void *effectMethods[]={effectDelete,effectTerm,effectReset,effectInfo,effectRelocation,effectRelocate,effectInit,effectExecute,effectSkip};

static HANDLE createMapping(const WCHAR*prefix,unsigned size){WCHAR name[96];lstrcpynW(name,prefix,70);unsigned at=lstrlenW(name),pid=GetCurrentProcessId(),n=0;WCHAR digits[16];do{digits[n++]=L'0'+pid%10;pid/=10;}while(pid);while(n)name[at++]=digits[--n];name[at]=0;return CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,size,name);}
API DWORD CaptureRecordPrepare(void*unused){
 (void)unused;if(!shared||!shared->active)return 1;
 if(!recording){recordingMapping=createMapping(L"Local\\ApexGroupsRecord2-",sizeof(Recording));if(!recordingMapping)return 2;
  recording=MapViewOfFile(recordingMapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Recording));if(!recording){CloseHandle(recordingMapping);recordingMapping=NULL;return 3;}
  recording->magic=0x32524741;recording->version=1;recording->capacity=REC_BLOCKS;recording->blockSize=sizeof(RecordBlock);recording->frequency=shared->frequency;
 }
 return 0;
}
static void put32(unsigned char*p,uint32_t x){memcpy(p,&x,4);}
static int buildBank(void){
 unsigned itemSize=cfg.bankVersion<=126?26:28;captureBankSize=8+cfg.headerSize+12+cfg.count*itemSize;captureBank=allocate(captureBankSize);if(!captureBank)return 0;
 memcpy(captureBank,"BKHD",4);put32(captureBank+4,cfg.headerSize);memcpy(captureBank+8,cfg.header,cfg.headerSize);put32(captureBank+12,cfg.bankId);
 if(cfg.bankVersion>=144&&cfg.headerSize>=40)memset(captureBank+32,0,16);
 unsigned at=8+cfg.headerSize;memcpy(captureBank+at,"HIRC",4);put32(captureBank+at+4,4+cfg.count*itemSize);put32(captureBank+at+8,cfg.count);at+=12;
 for(unsigned i=0;i<cfg.count;i++){unsigned char*p=captureBank+at;p[0]=cfg.bankVersion<=126?18:16;put32(p+1,itemSize-5);put32(p+5,fxIds[i]);put32(p+9,(0xa640u<<16)|(0x3f0u<<4)|3);put32(p+13,4);put32(p+17,i);at+=itemSize;}
 return 1;
}
API DWORD CaptureConfigure(void*data){
 if(!data)return 1;Config*in=data;if(in->magic!=0x43504741||in->version!=2||!in->count||in->count>ROUTES||in->headerSize<28||in->headerSize>64)return 2;
 if(CAPTURE_SDK_YEAR){if((CAPTURE_BANK_VERSION&&in->bankVersion!=CAPTURE_BANK_VERSION)||in->year!=CAPTURE_SDK_YEAR||(in->reserved?in->reserved:1u)!=CAPTURE_SDK_SERIES||in->bankVersion<120||in->bankVersion>512)return 3;}
 else if(!((in->bankVersion==140&&in->year==2021)||(in->bankVersion==150&&in->year==2023)))return 3;
 uint32_t ver;memcpy(&ver,in->header,4);if(ver!=in->bankVersion)return 4;
 if(shared&&shared->active)return 100;
 for(unsigned i=0;i<in->count;i++){if(in->slots[i]>3)return 5;for(unsigned j=0;j<i;j++)if(in->ids[j]==in->ids[i])return 6;}
 if(loaded&&(cfg.year!=in->year||cfg.count!=in->count||cfg.bankId!=in->bankId||memcmp(cfg.ids,in->ids,sizeof(cfg.ids))||memcmp(cfg.slots,in->slots,sizeof(cfg.slots))||memcmp(cfg.module,in->module,256)))return 111;
 cfg=*in;cfg.module[127]=0;configured=1;for(unsigned i=0;i<cfg.count;i++){busIds[i]=cfg.ids[i];fxIds[i]=cfg.bankId+0x100u+i;}
 return 0;
}
static void* findWwiseExport(HMODULE module,const char*name){void*p=(void*)GetProcAddress(module,name);if(p)return p;
if(!strcmp(name,"?SetBusEffect@SoundEngine@AK@@YA?AW4AKRESULT@@III@Z"))return (void*)GetProcAddress(module,"?SetBusEffect@SoundEngine@AK@@YA?AW4AKRESULT@@KKK@Z");
if(!strcmp(name,"?LoadBankMemoryCopy@SoundEngine@AK@@YA?AW4AKRESULT@@PEBXIAEAI@Z")){p=(void*)GetProcAddress(module,"?LoadBankMemoryCopy@SoundEngine@AK@@YA?AW4AKRESULT@@PEBXKAEAK@Z");if(p)return p;}
if(!strcmp(name,"?RegisterPlugin@SoundEngine@AK@@YA?AW4AKRESULT@@W4AkPluginType@@IIP6APEAVIAkPlugin@2@PEAVIAkPluginMemAlloc@2@@ZP6APEAVIAkPluginParam@2@1@ZP6A?AW43@AEAIPEAUAkDeviceDescription@@@Z@Z")){p=(void*)GetProcAddress(module,"?RegisterPlugin@SoundEngine@AK@@YA?AW4AKRESULT@@W4AkPluginType@@KKP6APEAVIAkPlugin@2@PEAVIAkPluginMemAlloc@2@@ZP6APEAVIAkPluginParam@2@1@ZP6A?AW43@AEAKPEAUAkDeviceDescription@@@Z@Z");if(p)return p;}
if(!strcmp(name,"?RegisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI2W4AkPluginType@@II@Z")){p=(void*)GetProcAddress(module,"?RegisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZK2W4AkPluginType@@KK@Z");if(p)return p;}
if(!strcmp(name,"?UnregisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI@Z"))return (void*)GetProcAddress(module,"?UnregisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZK@Z");
if(cfg.year<=2016){
 if(!strcmp(name,"?LoadBankMemoryCopy@SoundEngine@AK@@YA?AW4AKRESULT@@PEBXIAEAI@Z"))return (void*)GetProcAddress(module,"?LoadBank@SoundEngine@AK@@YA?AW4AKRESULT@@PEBXKAEAK@Z");
 if(!strcmp(name,"?RegisterPlugin@SoundEngine@AK@@YA?AW4AKRESULT@@W4AkPluginType@@IIP6APEAVIAkPlugin@2@PEAVIAkPluginMemAlloc@2@@ZP6APEAVIAkPluginParam@2@1@ZP6A?AW43@AEAIPEAUAkDeviceDescription@@@Z@Z"))return (void*)GetProcAddress(module,"?RegisterPlugin@SoundEngine@AK@@YA?AW4AKRESULT@@W4AkPluginType@@KKP6APEAVIAkPlugin@2@PEAVIAkPluginMemAlloc@2@@ZP6APEAVIAkPluginParam@2@1@Z@Z");
 if(!strcmp(name,"?RegisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI2W4AkPluginType@@II@Z"))return (void*)GetProcAddress(module,"?RegisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZK2@Z");
}
return NULL;}
API DWORD CaptureStart(void*unused){
 (void)unused;if(!configured)return 101;if(shared&&shared->active)return 100;HMODULE game=GetModuleHandleW(cfg.module[0]?cfg.module:NULL);if(!game)return 102;
 _Bool(*initialized)(void)=(void*)findWwiseExport(game,"?IsInitialized@SoundEngine@AK@@YA_NXZ");
 int(*settings)(void*)=(void*)findWwiseExport(game,"?GetAudioSettings@SoundEngine@AK@@YA?AW4AKRESULT@@AEAUAkAudioSettings@@@Z");
 int(*registerPlugin)(int,uint32_t,uint32_t,void*,void*,void*)=(void*)findWwiseExport(game,"?RegisterPlugin@SoundEngine@AK@@YA?AW4AKRESULT@@W4AkPluginType@@IIP6APEAVIAkPlugin@2@PEAVIAkPluginMemAlloc@2@@ZP6APEAVIAkPluginParam@2@1@ZP6A?AW43@AEAIPEAUAkDeviceDescription@@@Z@Z");
 int(*loadBank)(const void*,uint32_t,uint32_t*)=(void*)findWwiseExport(game,"?LoadBankMemoryCopy@SoundEngine@AK@@YA?AW4AKRESULT@@PEBXIAEAI@Z");
 setBusEffect=(void*)findWwiseExport(game,"?SetBusEffect@SoundEngine@AK@@YA?AW4AKRESULT@@III@Z");
 registerCallback=(void*)findWwiseExport(game,"?RegisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI2W4AkPluginType@@II@Z");
 unregisterCallback=(void*)findWwiseExport(game,"?UnregisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI@Z");
 if(!initialized||!settings||!registerPlugin||!loadBank||!setBusEffect||!registerCallback||!unregisterCallback||!initialized())return 102;
 uint32_t audioSettings[8]={0};if(settings(audioSettings)!=1||audioSettings[1]!=48000||!audioSettings[0]||audioSettings[0]>FRAMES)return 109;renderFrames=audioSettings[0];
 if(!shared){mapping=createMapping(L"Local\\ApexGroupsPcm-",sizeof(Shared));if(!mapping)return 103;Shared*view=MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Shared));if(!view)return 104;
  if(view->magic==0x37504341&&view->active){UnmapViewOfFile(view);CloseHandle(mapping);mapping=NULL;return 108;}
  shared=view;memset(shared,0,sizeof(*shared));shared->magic=0x37504341;shared->version=1;shared->pid=GetCurrentProcessId();LARGE_INTEGER f;QueryPerformanceFrequency(&f);shared->frequency=f.QuadPart;
 }
 if(!grid){gridMapping=createMapping(L"Local\\ApexGroups3-",sizeof(Grid));if(!gridMapping)return 103;grid=MapViewOfFile(gridMapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Grid));if(!grid)return 104;memset(grid,0,sizeof(*grid));grid->magic=0x47534241;grid->version=2;grid->pid=GetCurrentProcessId();grid->count=cfg.count;grid->monitor=-1;}
 paramTable=paramMethods;effectTable=effectMethods;
 if(!registered){shared->registerResult=cfg.year<=2016?((int(*)(int,uint32_t,uint32_t,void*,void*))registerPlugin)(3,0x3f0,0xa640,createEffect,createParams):registerPlugin(3,0x3f0,0xa640,createEffect,createParams,NULL);if(shared->registerResult!=1)return 105;registered=1;}
 if(!loaded){if(!buildBank())return 106;shared->bankResult=loadBank(captureBank,captureBankSize,&shared->bankId);if(cfg.year>2016){release(captureBank);captureBank=NULL;}if(shared->bankResult!=1)return 106;loaded=1;}
 memset(routeBuffers,0,sizeof(routeBuffers));shared->started=shared->heartbeat=now();
 if(!callbackRegistered){uint32_t locations=renderLocations();callbackResult=cfg.year<=2016?((int(*)(GlobalCallback,uint32_t,void*))registerCallback)(globalRender,locations,NULL):registerCallback(globalRender,locations,NULL,0,0,0);grid->callbackResult=callbackResult;if(callbackResult!=1)return 107;callbackRegistered=1;}
 shared->streams[0].rate=shared->streams[1].rate=48000;shared->streams[0].config=shared->streams[1].config=0x3102;shared->active=grid->active=1;
 shared->attachResults[0]=1;grid->generation++;
 for(unsigned i=0;i<cfg.count;i++){Group*g=&grid->groups[i];g->id=busIds[i];g->slot=cfg.slots[i];g->enabled=cfg.includes[i]!=0;routeAttach[i]=g->attach=setBusEffect(busIds[i],cfg.slots[i],fxIds[i]);if(routeAttach[i]!=1)shared->attachResults[0]=routeAttach[i];}
 return shared->attachResults[0]==1?0:110;
}
API DWORD CaptureExportMask(void*module){uint32_t found=0;
if(findWwiseExport(module,"?IsInitialized@SoundEngine@AK@@YA_NXZ"))found|=1u;
if(findWwiseExport(module,"?GetAudioSettings@SoundEngine@AK@@YA?AW4AKRESULT@@AEAUAkAudioSettings@@@Z"))found|=2u;
if(findWwiseExport(module,"?SetBusEffect@SoundEngine@AK@@YA?AW4AKRESULT@@III@Z"))found|=4u;
if(findWwiseExport(module,"?LoadBankMemoryCopy@SoundEngine@AK@@YA?AW4AKRESULT@@PEBXIAEAI@Z"))found|=8u;
if(findWwiseExport(module,"?RegisterPlugin@SoundEngine@AK@@YA?AW4AKRESULT@@W4AkPluginType@@IIP6APEAVIAkPlugin@2@PEAVIAkPluginMemAlloc@2@@ZP6APEAVIAkPluginParam@2@1@ZP6A?AW43@AEAIPEAUAkDeviceDescription@@@Z@Z"))found|=16u;
if(findWwiseExport(module,"?RegisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI2W4AkPluginType@@II@Z"))found|=32u;
if(findWwiseExport(module,"?UnregisterGlobalCallback@SoundEngine@AK@@YA?AW4AKRESULT@@P6AXPEAVIAkGlobalPluginContext@2@W4AkGlobalCallbackLocation@@PEAX@ZI@Z"))found|=64u;
return found;}
API DWORD CaptureHeartbeat(void*unused){(void)unused;if(!shared)return 1;shared->heartbeat=now();return 0;}
API DWORD CaptureStop(void*unused){
 (void)unused;if(!shared)return 0;shared->active=0;if(grid)grid->active=0;if(recording)InterlockedExchange(&recording->state,0);
 for(unsigned i=0;i<3;i++)shared->detachResults[i]=1;
 for(unsigned i=0;i<cfg.count;i++)if(routeAttach[i]==1){int result=setBusEffect(busIds[i],cfg.slots[i],0);if(result!=1)shared->detachResults[0]=result;routeAttach[i]=0;if(grid)grid->groups[i].attach=0;}
 if(callbackRegistered){int result=unregisterCallback(globalRender,renderLocations());if(result!=1)return result;callbackRegistered=0;}
 return shared->detachResults[0]==1?0:112;
}
API DWORD CaptureBankDump(void*path){
 if(!configured||loaded||shared||!path)return 1;if(!buildBank())return 2;
 HANDLE f=CreateFileW(path,GENERIC_WRITE,0,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);DWORD wrote=0;
 int ok=f!=INVALID_HANDLE_VALUE&&WriteFile(f,captureBank,captureBankSize,&wrote,NULL)&&wrote==captureBankSize;
 if(f!=INVALID_HANDLE_VALUE)CloseHandle(f);release(captureBank);captureBank=NULL;return ok?0:3;
}
API DWORD CaptureConfigSize(void*out){uint32_t*p=out;p[0]=sizeof(Config);p[1]=sizeof(Grid);p[2]=sizeof(Group);p[3]=offsetof(Grid,groups);return 0;}
API DWORD CaptureAdapterInfo(void*out){uint32_t*p=out;p[0]=1;p[1]=CAPTURE_BANK_VERSION;p[2]=CAPTURE_SDK_YEAR;p[3]=CAPTURE_SDK_SERIES;return 0;}
API DWORD CaptureSizes(void*out){uint32_t*s=out;s[0]=sizeof(Shared);s[1]=sizeof(Stream);s[2]=sizeof(Block);s[3]=sizeof(AudioBuffer);return 0;}
API DWORD CaptureSelfTest(void*unused){
 (void)unused;if(shared)return 1;shared=allocate(sizeof(Shared));grid=allocate(sizeof(Grid));if(!shared||!grid)return 2;cfg.count=2;cfg.year=2023;renderFrames=8;shared->active=grid->active=1;shared->frequency=1000000000;shared->heartbeat=now();grid->monitor=-1;grid->groups[0].enabled=1;
 float samples[16],before[16];for(unsigned i=0;i<16;i++)samples[i]=i*.01f;memcpy(before,samples,sizeof(samples));Effect e={effectMethods,0};AudioBuffer b={samples,0x3102,43,8,8};
 globalRender(NULL,16,NULL);effectExecute(&e,&b);globalRender(NULL,32,NULL);
 int ok=shared->streams[0].writes==1&&!memcmp(samples,before,sizeof(samples))&&shared->streams[0].blocks[0].pcm[FRAMES]==samples[8];
 grid->groups[0].enabled=0;grid->monitor=0;globalRender(NULL,16,NULL);effectExecute(&e,&b);globalRender(NULL,32,NULL);
 ok=ok&&shared->streams[0].blocks[1].pcm[FRAMES]==0&&shared->streams[1].blocks[0].pcm[FRAMES]==samples[8];
 globalRender(NULL,16,NULL);globalRender(NULL,32,NULL);ok=ok&&shared->streams[0].blocks[2].pcm[FRAMES]==0&&grid->groups[0].peak==0;
 grid->groups[0].enabled=1;float surround[48],original[48];for(unsigned i=0;i<48;i++)surround[i]=.01f*(i/8+1);memcpy(original,surround,sizeof(original));AudioBuffer multi={surround,0x60f106,43,8,8};
 globalRender(NULL,16,NULL);effectExecute(&e,&multi);globalRender(NULL,32,NULL);float expected=.01f+.03f*.70710678f+.04f*.5f+.06f*.5f;float diff=shared->streams[0].blocks[3].pcm[0]-expected;if(diff<0)diff=-diff;
 ok=ok&&diff<.000001f&&!memcmp(surround,original,sizeof(original));shared->active=0;globalRender(NULL,32,NULL);ok=ok&&shared->streams[0].writes==4;
 recording=allocate(sizeof(Recording));if(!recording)return 4;
 LARGE_INTEGER frequency;QueryPerformanceFrequency(&frequency);shared->frequency=frequency.QuadPart;
 shared->active=1;shared->heartbeat=now();recording->state=2;recording->limit=1000;
 recording->heartbeat=now()+shared->frequency;
 globalRender(NULL,16,NULL);globalRender(NULL,32,NULL);
 ok=ok&&recording->state==2&&recording->frame==8;
 recording->heartbeat=now()-shared->frequency*4;globalRender(NULL,32,NULL);
 ok=ok&&recording->state==3;
 /* Replay legacy callback locations with a real effect buffer. End-of-loop
  * location 32 must not emit a second block or advance the recording clock. */
 cfg.year=2016;shared->heartbeat=recording->heartbeat=now();recording->state=1;recording->writes=0;recording->limit=1000;LONG writesBefore=shared->streams[0].writes;
 globalRender(NULL,8,NULL);effectExecute(&e,&b);globalRender(NULL,16,NULL);
 ok=ok&&shared->streams[0].writes==writesBefore+1&&recording->writes==1&&recording->frame==8&&recording->blocks[0].pcm[1]==pcm16(samples[8]);
 globalRender(NULL,32,NULL);ok=ok&&shared->streams[0].writes==writesBefore+1&&recording->frame==8;
 release(recording);recording=NULL;
 release(shared);release(grid);shared=NULL;grid=NULL;memset(&cfg,0,sizeof(cfg));memset(routeBuffers,0,sizeof(routeBuffers));return ok?0:3;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){(void)reserved;if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(instance);return TRUE;}
