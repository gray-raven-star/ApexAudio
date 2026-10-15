/* Read-only Unreal AudioMixer Submix listeners. Addresses and names come from
 * a checksum-bound game package. No code patches, gain or game audio changes. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <stddef.h>
#define API __declspec(dllexport)
#define ROUTES 64
#define FRAMES 2048
#define CAPACITY 8192
typedef struct {volatile LONG sequence;uint32_t frames,channels,config;uint64_t tick;float pcm[4096];} Block;
typedef struct {volatile LONG writes,initCalls,termCalls,invalid;uint32_t rate,config;Block blocks[64];} Stream;
typedef struct {uint32_t magic,version,pid,bankId;volatile LONG active;int32_t results[9];uint64_t frequency,started,heartbeat;Stream streams[3];} Source;
typedef struct {volatile LONG sequence;uint32_t route,frame,frames;float peak;uint32_t reserved;uint64_t tick;int16_t pcm[4096];} RecordBlock;
typedef struct {uint32_t magic,version;volatile LONG active,writes;uint32_t frame,limit,capacity,blockSize;uint64_t heartbeat,frequency;uint32_t reserved[4];RecordBlock blocks[CAPACITY];} Recording;
typedef struct {uint32_t magic,version;uint64_t engineRva,nameBlocksRva,vtableRva,registerRva,unregisterRva;uint32_t holderOffset,deviceOffset,count,reserved;WCHAR names[ROUTES][96];} Configuration;
typedef struct {void**vtable;void*weak[2];unsigned index;} Listener;
typedef struct {void*object,*mixer;Listener listener;volatile LONG selected;unsigned frames;double clock;float pcm[4096];} Route;
typedef struct {WCHAR*data;int count,capacity;} FString;
_Static_assert(sizeof(Source)==3150488,"source ABI");
_Static_assert(sizeof(RecordBlock)==8224,"record ABI");
static Configuration cfg;static Source*source;static Recording*recording;static HANDLE sourceMap,recordMap,bindingsMap;static uint32_t*bindings;
static Route routes[ROUTES];static void*device;static uint8_t*module;
static void(*registerListener)(void*,void*,void*),(*unregisterListener)(void*,void*,void*);
static SRWLOCK audioLock=SRWLOCK_INIT;static double mixClock=-1;static uint64_t mixTick;static unsigned mixFrames;
static uint64_t now(void){LARGE_INTEGER q;QueryPerformanceCounter(&q);return q.QuadPart;}
static int readable(const void*p,size_t size){MEMORY_BASIC_INFORMATION m;return p&&VirtualQuery(p,&m,sizeof(m))&&m.State==MEM_COMMIT&&!(m.Protect&(PAGE_NOACCESS|PAGE_GUARD))&&(uintptr_t)p+size<=(uintptr_t)m.BaseAddress+m.RegionSize;}
static void*ptr(const void*p){return readable(p,sizeof(void*))?*(void**)p:NULL;}
static int objectName(void*object,WCHAR*out){
 if(!readable(object,32))return 0;uint32_t index=*(uint32_t*)((uint8_t*)object+24);
 if(index>>16>=8192)return 0;uint8_t*block=ptr(module+cfg.nameBlocksRva+(index>>16)*8);uint8_t*entry=block+(index&65535u)*2;
 if(!readable(entry,2))return 0;uint16_t header=*(uint16_t*)entry;unsigned length=header>>6;
 if(!length||length>=96||!readable(entry+2,length*(header&1?2:1)))return 0;
 for(unsigned i=0;i<length;i++)out[i]=header&1?((WCHAR*)(entry+2))[i]:entry[2+i];out[length]=0;return 1;
}
static HANDLE mapping(const WCHAR*prefix,unsigned size){WCHAR name[96];lstrcpyW(name,prefix);unsigned pid=GetCurrentProcessId(),at=lstrlenW(name),n=0;WCHAR digits[16];do{digits[n++]=L'0'+pid%10;pid/=10;}while(pid);while(n)name[at++]=digits[--n];name[at]=0;return CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,size,name);}
static int createBuffers(void){
 if(!source){sourceMap=mapping(L"Local\\ApexUnrealMix-",sizeof(Source));if(sourceMap)source=MapViewOfFile(sourceMap,FILE_MAP_ALL_ACCESS,0,0,sizeof(Source));}
 if(!recording){recordMap=mapping(L"Local\\ApexUnrealTracks-",sizeof(Recording));if(recordMap)recording=MapViewOfFile(recordMap,FILE_MAP_ALL_ACCESS,0,0,sizeof(Recording));}
 if(!bindings){bindingsMap=mapping(L"Local\\ApexUnrealBindings-",ROUTES*8);if(bindingsMap)bindings=MapViewOfFile(bindingsMap,FILE_MAP_ALL_ACCESS,0,0,ROUTES*8);}
 if(!source||!recording||!bindings)return 0;memset(bindings,0,ROUTES*8);
 memset(source,0,sizeof(*source));memset(recording,0,sizeof(*recording));source->magic=0x37504341;source->version=1;source->pid=GetCurrentProcessId();LARGE_INTEGER f;QueryPerformanceFrequency(&f);source->frequency=f.QuadPart;source->started=source->heartbeat=now();source->streams[0].rate=48000;
 recording->magic=0x31544641;recording->version=1;recording->capacity=CAPACITY;recording->blockSize=sizeof(RecordBlock);recording->frequency=source->frequency;recording->active=source->active=1;mixClock=-1;mixFrames=0;return 1;
}
static int16_t pcm16(float v){if(!(v==v))v=0;if(v>1)v=1;if(v< -1)v=-1;return (int16_t)(v*32767);}
static void publishMix(void){
 if(!mixFrames)return;Stream*s=&source->streams[0];Block*b=&s->blocks[(unsigned)s->writes%64];InterlockedIncrement(&b->sequence);b->frames=mixFrames;b->channels=2;b->config=0x3102;b->tick=mixTick;memset(b->pcm,0,sizeof(b->pcm));
 for(unsigned i=0;i<cfg.count;i++){Route*r=&routes[i];if(r->selected&&r->clock==mixClock){unsigned frames=r->frames<mixFrames?r->frames:mixFrames;for(unsigned j=0;j<frames;j++){b->pcm[j]+=r->pcm[j];b->pcm[FRAMES+j]+=r->pcm[FRAMES+j];}}}
 InterlockedIncrement(&b->sequence);InterlockedIncrement(&s->writes);recording->frame+=mixFrames;
}
static void onBuffer(Listener*self,void*owner,float*input,int samples,int channels,int rate,double clock){
 (void)owner;if(!source||!source->active||!input||samples<=0||channels<1||channels>16||samples%channels)return;
 unsigned frames=samples/channels;if(frames>FRAMES||rate!=48000){InterlockedIncrement((volatile LONG*)&source->results[1]);return;}
 uint64_t tick=now(),heartbeat=source->heartbeat;if(tick>heartbeat&&tick-heartbeat>source->frequency*10){source->active=0;return;}
 if(self->index>=cfg.count)return;AcquireSRWLockExclusive(&audioLock);
 if(clock!=mixClock){publishMix();mixClock=clock;mixTick=tick-(uint64_t)frames*source->frequency/rate;mixFrames=frames;}
 Route*r=&routes[self->index];r->frames=frames;r->clock=clock;float peak=0;
 for(unsigned i=0;i<frames;i++){
  float*p=input+i*channels,a=p[0],b=channels>1?p[1]:a;
  if(channels>2){a+=p[2]*.70710678f;b+=p[2]*.70710678f;}if(channels>3){a+=p[3]*.5f;b+=p[3]*.5f;}
  for(int c=4;c<channels;c++){if(c&1)b+=p[c]*.5f;else a+=p[c]*.5f;}
  r->pcm[i]=a;r->pcm[FRAMES+i]=b;float pa=a<0?-a:a,pb=b<0?-b:b;if(pa>peak)peak=pa;if(pb>peak)peak=pb;
 }
 if(recording->active&&peak>.00003f){RecordBlock*b=&recording->blocks[(unsigned)recording->writes%CAPACITY];InterlockedIncrement(&b->sequence);b->route=self->index;b->frame=recording->frame;b->frames=frames;b->peak=peak;b->tick=tick-(uint64_t)frames*source->frequency/rate;
  for(unsigned i=0;i<frames;i++){b->pcm[i*2]=pcm16(r->pcm[i]);b->pcm[i*2+1]=pcm16(r->pcm[FRAMES+i]);}InterlockedIncrement(&b->sequence);InterlockedIncrement(&recording->writes);
 }
 InterlockedIncrement((volatile LONG*)&source->results[2]);ReleaseSRWLockExclusive(&audioLock);
}
static const FString*listenerName(Listener*self){(void)self;static WCHAR text[]=L"Apex Audio";static FString name={text,11,11};return &name;}
static _Bool rendering(Listener*self){(void)self;return 0;}
/* This verified AudioMixer interface invokes OnNewSubmixBuffer at vtable[0].
 * It does not have a leading virtual destructor. The device retains raw
 * listener pointers; these listeners have module lifetime. */
static void*methods[]={onBuffer,listenerName,rendering};
API DWORD UnrealCaptureStop(void*unused){
 (void)unused;AcquireSRWLockExclusive(&audioLock);if(source)source->active=0;if(recording)recording->active=0;ReleaseSRWLockExclusive(&audioLock);
 if(readable(device,0xb90)&&ptr(device)==module+cfg.vtableRva&&unregisterListener)for(unsigned i=0;i<cfg.count;i++)if(routes[i].object&&readable(routes[i].object,32)){unregisterListener(device,&routes[i].listener,routes[i].object);routes[i].object=NULL;}
 device=NULL;return 0;
}
API DWORD UnrealCaptureStart(void*input){
 if(!input)return 1;Configuration*c=input;if(c->magic!=0x43554141||c->version!=1||!c->count||c->count>ROUTES)return 2;
 UnrealCaptureStop(NULL);cfg=*c;module=(uint8_t*)GetModuleHandleW(NULL);
 void*engine=ptr(module+cfg.engineRva);void*holder=engine?ptr((uint8_t*)engine+cfg.holderOffset):NULL;device=holder?ptr((uint8_t*)holder+cfg.deviceOffset):NULL;
 if(!readable(device,0xb90)||ptr(device)!=module+cfg.vtableRva||!(*(uint8_t*)((uint8_t*)device+0x412)&0x20)){device=NULL;return 3;}
 registerListener=(void*)(module+cfg.registerRva);unregisterListener=(void*)(module+cfg.unregisterRva);void*entries=ptr((uint8_t*)device+0xb40);unsigned count=*(uint32_t*)((uint8_t*)device+0xb48);
 if(count>1024||!readable(entries,count*32)){device=NULL;return 4;}
 memset(routes,0,sizeof(routes));WCHAR name[96];void*mainObjects=ptr((uint8_t*)device+0x980);
 for(unsigned i=0;i<cfg.count;i++){
  routes[i].listener.vtable=methods;routes[i].listener.index=i;cfg.names[i][95]=0;
  for(unsigned j=0;j<count;j++){void*object=ptr((uint8_t*)entries+j*32);if(objectName(object,name)&&!lstrcmpW(name,cfg.names[i])){routes[i].object=object;routes[i].mixer=ptr((uint8_t*)entries+j*32+8);break;}}
  if(!routes[i].object&&readable(mainObjects,24))for(unsigned j=0;j<3;j++){void*object=ptr((uint8_t*)mainObjects+j*8);if(objectName(object,name)&&!lstrcmpW(name,cfg.names[i])){routes[i].object=object;void*mainMix=ptr((uint8_t*)device+0x990);routes[i].mixer=mainMix?ptr((uint8_t*)mainMix+j*16):NULL;break;}}
 }
 if(!createBuffers()){device=NULL;return 5;}unsigned resolved=0;
 for(unsigned i=0;i<cfg.count;i++)if(routes[i].object){
  bindings[i]=1;void*parent=readable(routes[i].mixer,32)?ptr((uint8_t*)routes[i].mixer+24):NULL;
  if(parent)for(unsigned j=0;j<cfg.count;j++)if(j!=i&&routes[j].mixer==parent){bindings[ROUTES+i]=j+1;break;}
  registerListener(device,&routes[i].listener,routes[i].object);resolved++;
 }
 source->results[0]=resolved;if(!resolved){UnrealCaptureStop(NULL);return 6;}return 0;
}
API DWORD UnrealCaptureSelect(void*input){if(!source||!source->active||!input)return 1;int*mask=input;AcquireSRWLockExclusive(&audioLock);for(unsigned i=0;i<cfg.count;i++)routes[i].selected=mask[i]!=0;ReleaseSRWLockExclusive(&audioLock);return 0;}
API DWORD UnrealCaptureSelfTest(void*unused){
 (void)unused;if(source)return 1;cfg.count=2;if(!createBuffers())return 2;routes[0].listener.vtable=methods;routes[1].listener.vtable=methods;routes[0].listener.index=0;routes[1].listener.index=1;routes[0].selected=1;
 /* Dispatch through the same slot used by the verified engine, rather than
  * calling onBuffer directly and bypassing the listener ABI. */
 typedef void(*BufferCallback)(Listener*,void*,float*,int,int,int,double);
 BufferCallback first=(BufferCallback)routes[0].listener.vtable[0],second=(BufferCallback)routes[1].listener.vtable[0];
 float a[]={.25f,.125f,.5f,.375f},b[]={.1f,.2f,.3f,.4f};first(&routes[0].listener,NULL,a,4,2,48000,1);second(&routes[1].listener,NULL,b,4,2,48000,1);first(&routes[0].listener,NULL,a,4,2,48000,2);
 int ok=source->streams[0].writes==1&&source->streams[0].blocks[0].pcm[0]==.25f&&source->streams[0].blocks[0].pcm[FRAMES]==.125f&&recording->writes==3;
 UnmapViewOfFile(source);UnmapViewOfFile(recording);UnmapViewOfFile(bindings);CloseHandle(sourceMap);CloseHandle(recordMap);CloseHandle(bindingsMap);source=NULL;recording=NULL;bindings=NULL;return ok?0:7;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){(void)reserved;if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(instance);return TRUE;}
