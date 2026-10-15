/* Temporary input shim for the user's X-Haptics v2.0 process.
 * Uses documented WASAPI COM interfaces. No executable-code patches or files
 * are modified. CoCreateInstance's import and five COM method pointers are
 * temporarily redirected and restored on stop. Original headphone audio is
 * untouched: only X-Haptics' private capture buffer pointer is substituted.
 */
#define WIN32_LEAN_AND_MEAN
#define COBJMACROS
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <stdint.h>
#include <string.h>
#define API __declspec(dllexport)
#define FRAMES 2048
#define CHANNELS 2
#define BLOCKS 64
typedef struct {volatile LONG sequence;uint32_t frames,channels,config;uint64_t tick;float pcm[4096];} Block;
typedef struct {volatile LONG writes,initCalls,termCalls,invalid;uint32_t rate,config;Block blocks[64];} Stream;
typedef struct {uint32_t magic,version,pid,bankId;volatile LONG active;int32_t registerResult,bankResult,attachResults[3],detachResults[3];uint64_t frequency,started,heartbeat;Stream streams[3];} Source;
typedef struct {uint32_t magic,version,gamePid,xhPid;volatile LONG active,hookCount,enumeratorCalls,deviceCalls,clientCalls,captureCalls,bufferCalls,replacedPackets,copiedBlocks,underruns,staleDropped,errors;uint32_t rate,channels,bits;float peak;uint64_t framesDelivered,lastSourceTick,lastReplaceTick,controllerTick,processingTicks;uint32_t maxUs,lastUs;uint64_t queueAgeUs;float gain,inputPeak;uint32_t syntheticPackets,reserved;} Status;
typedef struct {void **slot;void *original;void *replacement;DWORD protection;} Hook;
static Hook hooks[64];static volatile LONG hookCount;
static Source *source;static Status *status;static HANDLE sourceMap,statusMap;
static uint32_t sourcePosition,blockOffset,blockFrames;static Block currentBlock;
static float output[65536];
typedef struct {void*self;uint64_t next;UINT32 acquired;} Clock;
static Clock clocks[32];static unsigned clockCount;static SRWLOCK transportLock=SRWLOCK_INIT;
static HANDLE eventCopy,eventClient,pumpThread,pumpStop;
_Static_assert(sizeof(Status)==152,"status ABI");
_Static_assert(sizeof(Source)==3150488,"source ABI");
static Clock* clockFor(void*self){for(unsigned i=0;i<clockCount;i++)if(clocks[i].self==self)return &clocks[i];return NULL;}
static void trackClock(void*self){Clock*c=clockFor(self);if(!c&&clockCount<32)c=&clocks[clockCount++];if(c){c->self=self;c->next=0;c->acquired=0;}}
static DWORD WINAPI pump(void*arg){(void)arg;while(WaitForSingleObject(pumpStop,5)==WAIT_TIMEOUT){AcquireSRWLockShared(&transportLock);if(status&&status->active&&eventCopy)SetEvent(eventCopy);ReleaseSRWLockShared(&transportLock);}return 0;}
static void resetRead(void){sourcePosition=source?source->streams[0].writes:0;blockFrames=blockOffset=0;for(unsigned i=0;i<clockCount;i++){clocks[i].next=0;clocks[i].acquired=0;}}

static uint64_t now(void){LARGE_INTEGER q;QueryPerformanceCounter(&q);return q.QuadPart;}
static int same(REFIID a,const GUID*b){return !memcmp(a,b,sizeof(GUID));}
static void *original(void*self,unsigned index){void**slot=*(void***)self+index;LONG n=hookCount;for(LONG i=0;i<n;i++)if(hooks[i].slot==slot)return hooks[i].original;return NULL;}
static int patch(void**slot,void*replacement){
 for(LONG i=0;i<hookCount;i++)if(hooks[i].slot==slot){if(hooks[i].replacement!=replacement)return 0;if(*slot==replacement)return 1;if(*slot!=hooks[i].original)return 0;DWORD old,ignored;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&old))return 0;InterlockedCompareExchangePointer(slot,replacement,hooks[i].original);VirtualProtect(slot,sizeof(void*),old,&ignored);return 1;}
 if(hookCount>=64)return 0;DWORD old;if(!VirtualProtect(slot,sizeof(void*),PAGE_READWRITE,&old))return 0;
 LONG i=hookCount;hooks[i].slot=slot;hooks[i].original=*slot;hooks[i].replacement=replacement;hooks[i].protection=old;InterlockedIncrement(&hookCount);
 InterlockedExchangePointer(slot,replacement);DWORD ignored;VirtualProtect(slot,sizeof(void*),old,&ignored);if(status)status->hookCount=hookCount;return 1;
}
static void restore(void){for(LONG i=hookCount-1;i>=0;i--){DWORD old,ignored;if(VirtualProtect(hooks[i].slot,sizeof(void*),PAGE_READWRITE,&old)){InterlockedCompareExchangePointer(hooks[i].slot,hooks[i].original,hooks[i].replacement);VirtualProtect(hooks[i].slot,sizeof(void*),old,&ignored);}}}
static const GUID enumId={0xa95664d2,0x9614,0x4f35,{0xa7,0x46,0xde,0x8d,0xb6,0x36,0x17,0xe6}};
static const GUID clientId={0x1cb9ad4c,0xdbfa,0x4c32,{0xb1,0x78,0xc2,0xf5,0x68,0xa7,0x03,0xb2}};
static const GUID captureId={0xc8adbd64,0xe71e,0x48a0,{0xa4,0xde,0x18,0x5c,0x39,0x5c,0xd3,0x17}};
static const GUID floatId={3,0,0x0010,{0x80,0,0,0xaa,0,0x38,0x9b,0x71}};

static HRESULT STDMETHODCALLTYPE hookPacket(IAudioCaptureClient*self,UINT32*frames){
 if(!frames)return E_POINTER;AcquireSRWLockShared(&transportLock);Clock*c=clockFor(self);
 if(!c){ReleaseSRWLockShared(&transportLock);HRESULT(STDMETHODCALLTYPE*fn)(IAudioCaptureClient*,UINT32*)=original(self,5);return fn?fn(self,frames):E_FAIL;}
 uint64_t tick=now(),frequency=source?source->frequency:10000000;if(!c->next)c->next=tick+frequency/100;
 if(tick>c->next+frequency/10)c->next=tick;
 *frames=c->acquired?c->acquired:(tick>=c->next?480:0);ReleaseSRWLockShared(&transportLock);return S_OK;
}
static HRESULT STDMETHODCALLTYPE hookRelease(IAudioCaptureClient*self,UINT32 frames){
 AcquireSRWLockShared(&transportLock);Clock*c=clockFor(self);if(!c){ReleaseSRWLockShared(&transportLock);HRESULT(STDMETHODCALLTYPE*fn)(IAudioCaptureClient*,UINT32)=original(self,4);return fn?fn(self,frames):E_FAIL;}
 HRESULT hr=S_OK;if(!c->acquired)hr=AUDCLNT_E_OUT_OF_ORDER;else if(frames&&frames!=c->acquired)hr=AUDCLNT_E_INVALID_SIZE;else{if(frames)c->next+=(source?source->frequency:10000000)/100;c->acquired=0;}ReleaseSRWLockShared(&transportLock);return hr;
}
static HRESULT STDMETHODCALLTYPE hookBuffer(IAudioCaptureClient*self,BYTE**data,UINT32*frames,DWORD*flags,UINT64*devicePosition,UINT64*qpcPosition){
 if(!data||!frames||!flags)return E_POINTER;AcquireSRWLockShared(&transportLock);Clock*c=clockFor(self);
 if(!c){ReleaseSRWLockShared(&transportLock);HRESULT(STDMETHODCALLTYPE*fn)(IAudioCaptureClient*,BYTE**,UINT32*,DWORD*,UINT64*,UINT64*)=original(self,3);return fn?fn(self,data,frames,flags,devicePosition,qpcPosition):E_FAIL;}
 if(c->acquired){ReleaseSRWLockShared(&transportLock);return AUDCLNT_E_OUT_OF_ORDER;}
 uint64_t tick=now(),frequency=source?source->frequency:10000000;if(!c->next)c->next=tick+frequency/100;
 if(tick<c->next){*frames=0;*data=NULL;*flags=AUDCLNT_BUFFERFLAGS_SILENT;ReleaseSRWLockShared(&transportLock);return AUDCLNT_S_BUFFER_EMPTY;}
 if(tick>c->next+frequency/10)c->next=tick;
 *frames=c->acquired=480;*flags=0;if(devicePosition)*devicePosition=status?status->framesDelivered:0;if(qpcPosition)*qpcPosition=tick*10000000/frequency;
 if(status)InterlockedIncrement(&status->bufferCalls);
 unsigned done=0;float peak=0,inputPeak=0;BOOL active=status&&status->active&&source&&source->active&&status->rate==48000&&status->channels==2&&status->bits==32;
 uint64_t controllerTick=status?status->controllerTick:0;
 if(active&&tick>controllerTick&&tick-controllerTick>frequency*5){status->active=0;active=FALSE;}
 if(active)source->heartbeat=tick;
 if(active){
 /* Compare the next unread sample's timestamp, rather than the start of an
  * already partly consumed block. Otherwise normal 42 ms chunks lose tails. */
 uint64_t pendingTick=currentBlock.tick+(uint64_t)blockOffset*source->frequency/48000;
 if(blockOffset<blockFrames&&tick>pendingTick&&tick-pendingTick>source->frequency/10){blockFrames=blockOffset=0;InterlockedIncrement(&status->staleDropped);}
 while(done<*frames){
  if(blockOffset>=blockFrames){
   LONG written=source->streams[0].writes;
   if((uint32_t)written-sourcePosition>64){status->staleDropped+=(uint32_t)written-sourcePosition-64;sourcePosition=(uint32_t)written-64;}
   if(sourcePosition>=(uint32_t)written)break;
   Block*b=&source->streams[0].blocks[sourcePosition%64];LONG seq=b->sequence;if(seq&1)break;
   memcpy(&currentBlock,b,sizeof(currentBlock));MemoryBarrier();if(seq!=b->sequence)break;sourcePosition++;
   if(currentBlock.channels!=2||currentBlock.frames>2048||!currentBlock.frames){InterlockedIncrement(&status->errors);continue;}
   if(tick>currentBlock.tick&&tick-currentBlock.tick>source->frequency/10){InterlockedIncrement(&status->staleDropped);continue;}
   blockFrames=currentBlock.frames;blockOffset=0;InterlockedIncrement(&status->copiedBlocks);status->lastSourceTick=currentBlock.tick;
  }
  unsigned count=blockFrames-blockOffset;if(count>*frames-done)count=*frames-done;
  for(unsigned i=0;i<count;i++)for(unsigned ch=0;ch<2;ch++){float raw=currentBlock.pcm[ch*2048+blockOffset+i];float av=raw<0?-raw:raw;if(av>inputPeak)inputPeak=av;float v=raw*status->gain;if(!(v==v))v=0;if(v>1)v=1;if(v< -1)v=-1;output[(done+i)*2+ch]=v;av=v<0?-v:v;if(av>peak)peak=av;}
  blockOffset+=count;done+=count;
 }
 }
 if(done<*frames){memset(output+done*2,0,(*frames-done)*2*sizeof(float));if(status&&active)InterlockedIncrement(&status->underruns);}
 *data=(BYTE*)output;if(peak<=.000001f)*flags|=AUDCLNT_BUFFERFLAGS_SILENT;
 if(status){status->peak=peak;status->inputPeak=inputPeak;status->framesDelivered+=*frames;status->lastReplaceTick=tick;InterlockedIncrement(&status->replacedPackets);InterlockedIncrement((volatile LONG*)&status->syntheticPackets);uint64_t cost=now()-tick;status->processingTicks+=cost;unsigned us=(unsigned)(cost*1000000/frequency);status->lastUs=us;if(us>status->maxUs)status->maxUs=us;if(done&&status->lastSourceTick)status->queueAgeUs=(tick-status->lastSourceTick)*1000000/frequency;}
 ReleaseSRWLockShared(&transportLock);return S_OK;
}
static HRESULT STDMETHODCALLTYPE hookService(IAudioClient*self,REFIID iid,void**out){
 HRESULT(STDMETHODCALLTYPE*fn)(IAudioClient*,REFIID,void**)=original(self,14);if(!fn)return E_FAIL;HRESULT hr=fn(self,iid,out);
 if(SUCCEEDED(hr)&&out&&*out&&same(iid,&captureId)&&status&&status->active){InterlockedIncrement(&status->captureCalls);AcquireSRWLockExclusive(&transportLock);trackClock(*out);ReleaseSRWLockExclusive(&transportLock);patch(*(void***)*out+3,hookBuffer);patch(*(void***)*out+4,hookRelease);patch(*(void***)*out+5,hookPacket);}return hr;
}
static HRESULT STDMETHODCALLTYPE hookEvent(IAudioClient*self,HANDLE event){
 HRESULT(STDMETHODCALLTYPE*fn)(IAudioClient*,HANDLE)=original(self,13);HRESULT hr=fn?fn(self,event):E_FAIL;if(SUCCEEDED(hr)){HANDLE copy=NULL;DuplicateHandle(GetCurrentProcess(),event,GetCurrentProcess(),&copy,EVENT_MODIFY_STATE,FALSE,0);AcquireSRWLockExclusive(&transportLock);if(eventCopy)CloseHandle(eventCopy);eventCopy=copy;eventClient=(HANDLE)self;ReleaseSRWLockExclusive(&transportLock);}return hr;
}
static HRESULT STDMETHODCALLTYPE hookClientStop(IAudioClient*self){
 HRESULT(STDMETHODCALLTYPE*fn)(IAudioClient*)=original(self,11);HRESULT hr=fn?fn(self):E_FAIL;AcquireSRWLockExclusive(&transportLock);if(eventClient==(HANDLE)self){if(eventCopy)CloseHandle(eventCopy);eventCopy=NULL;eventClient=NULL;}ReleaseSRWLockExclusive(&transportLock);return hr;
}
static HRESULT STDMETHODCALLTYPE hookInitialize(IAudioClient*self,AUDCLNT_SHAREMODE mode,DWORD flags,REFERENCE_TIME duration,REFERENCE_TIME periodicity,const WAVEFORMATEX*fmt,LPCGUID session){
 HRESULT(STDMETHODCALLTYPE*fn)(IAudioClient*,AUDCLNT_SHAREMODE,DWORD,REFERENCE_TIME,REFERENCE_TIME,const WAVEFORMATEX*,LPCGUID)=original(self,3);if(!fn)return E_FAIL;
 HRESULT hr=fn(self,mode,flags,duration,periodicity,fmt,session);
 if(SUCCEEDED(hr)&&status&&status->active&&(flags&AUDCLNT_STREAMFLAGS_LOOPBACK)&&fmt){patch(*(void***)self+13,hookEvent);patch(*(void***)self+11,hookClientStop);status->rate=fmt->nSamplesPerSec;status->channels=fmt->nChannels;status->bits=fmt->wBitsPerSample;if(fmt->wFormatTag!=WAVE_FORMAT_IEEE_FLOAT&&(fmt->wFormatTag!=WAVE_FORMAT_EXTENSIBLE||fmt->cbSize<22||!same(&((const WAVEFORMATEXTENSIBLE*)fmt)->SubFormat,&floatId)))status->bits=0;}return hr;
}
static HRESULT STDMETHODCALLTYPE hookActivate(IMMDevice*self,REFIID iid,DWORD context,PROPVARIANT*params,void**out){
 HRESULT(STDMETHODCALLTYPE*fn)(IMMDevice*,REFIID,DWORD,PROPVARIANT*,void**)=original(self,3);if(!fn)return E_FAIL;HRESULT hr=fn(self,iid,context,params,out);
 if(SUCCEEDED(hr)&&out&&*out&&same(iid,&clientId)&&status&&status->active){InterlockedIncrement(&status->clientCalls);patch(*(void***)*out+3,hookInitialize);patch(*(void***)*out+14,hookService);}return hr;
}
static void patchDevice(IMMDevice*d){if(d&&status&&status->active){InterlockedIncrement(&status->deviceCalls);patch(*(void***)d+3,hookActivate);}}
static HRESULT STDMETHODCALLTYPE hookItem(IMMDeviceCollection*self,UINT index,IMMDevice**out){HRESULT(STDMETHODCALLTYPE*fn)(IMMDeviceCollection*,UINT,IMMDevice**)=original(self,4);if(!fn)return E_FAIL;HRESULT hr=fn(self,index,out);if(SUCCEEDED(hr)&&out)patchDevice(*out);return hr;}
static HRESULT STDMETHODCALLTYPE hookEndpoints(IMMDeviceEnumerator*self,EDataFlow flow,DWORD state,IMMDeviceCollection**out){HRESULT(STDMETHODCALLTYPE*fn)(IMMDeviceEnumerator*,EDataFlow,DWORD,IMMDeviceCollection**)=original(self,3);if(!fn)return E_FAIL;HRESULT hr=fn(self,flow,state,out);if(SUCCEEDED(hr)&&out&&*out&&status&&status->active)patch(*(void***)*out+4,hookItem);return hr;}
static HRESULT STDMETHODCALLTYPE hookDefault(IMMDeviceEnumerator*self,EDataFlow flow,ERole role,IMMDevice**out){HRESULT(STDMETHODCALLTYPE*fn)(IMMDeviceEnumerator*,EDataFlow,ERole,IMMDevice**)=original(self,4);if(!fn)return E_FAIL;HRESULT hr=fn(self,flow,role,out);if(SUCCEEDED(hr)&&out)patchDevice(*out);return hr;}
static HRESULT WINAPI hookCreate(REFCLSID cls,LPUNKNOWN outer,DWORD context,REFIID iid,LPVOID*out){
 HRESULT(WINAPI*fn)(REFCLSID,LPUNKNOWN,DWORD,REFIID,LPVOID*)=NULL;for(LONG i=0;i<hookCount;i++)if(hooks[i].replacement==hookCreate){fn=hooks[i].original;break;}if(!fn)return E_FAIL;HRESULT hr=fn(cls,outer,context,iid,out);
 if(SUCCEEDED(hr)&&out&&*out&&same(iid,&enumId)&&status&&status->active){InterlockedIncrement(&status->enumeratorCalls);patch(*(void***)*out+3,hookEndpoints);patch(*(void***)*out+4,hookDefault);}return hr;
}
static void mappingName(WCHAR*out,const WCHAR*prefix,unsigned pid){lstrcpyW(out,prefix);unsigned at=lstrlenW(out),n=0;WCHAR d[16];do{d[n++]=L'0'+pid%10;pid/=10;}while(pid);while(n)out[at++]=d[--n];out[at]=0;}
static DWORD bindNamedSource(unsigned gamePid,unsigned producerPid,const WCHAR*mapping){
 WCHAR name[64];HANDLE candidateMap=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mapping);if(!candidateMap)return 102;
 Source*candidate=MapViewOfFile(candidateMap,FILE_MAP_ALL_ACCESS,0,0,sizeof(Source));if(!candidate||candidate->magic!=0x37504341||candidate->version!=1||candidate->pid!=producerPid||!candidate->active||!candidate->frequency||candidate->streams[0].rate!=48000){if(candidate)UnmapViewOfFile(candidate);CloseHandle(candidateMap);return 103;}
 AcquireSRWLockExclusive(&transportLock);if(status)status->active=0;Source*old=source;HANDLE oldMap=sourceMap;source=candidate;sourceMap=candidateMap;
 if(!status){mappingName(name,L"Local\\ApexGroupsXhV4-",GetCurrentProcessId());statusMap=CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,sizeof(Status),name);if(statusMap)status=MapViewOfFile(statusMap,FILE_MAP_ALL_ACCESS,0,0,sizeof(Status));}
 if(!status){source=old;sourceMap=oldMap;ReleaseSRWLockExclusive(&transportLock);UnmapViewOfFile(candidate);CloseHandle(candidateMap);return 104;}
 memset(status,0,sizeof(*status));status->magic=0x58484241;status->version=4;status->gamePid=gamePid;status->xhPid=GetCurrentProcessId();status->controllerTick=now();status->gain=1;status->active=1;resetRead();ReleaseSRWLockExclusive(&transportLock);
 if(old)UnmapViewOfFile(old);if(oldMap)CloseHandle(oldMap);
 return 0;
}
static DWORD bindSource(unsigned gamePid,unsigned inputPid){
 WCHAR name[64];mappingName(name,inputPid?L"Local\\ApexGroupsInput-":L"Local\\ApexGroupsPcm-",inputPid?inputPid:gamePid);
 return bindNamedSource(gamePid,inputPid?inputPid:gamePid,name);
}
static DWORD startBridge(HMODULE host){
 if(!pumpStop)pumpStop=CreateEventW(NULL,TRUE,FALSE,NULL);if(!pumpThread){ResetEvent(pumpStop);pumpThread=CreateThread(NULL,0,pump,NULL,0,NULL);if(!pumpThread){status->active=0;return 113;}}
 if(hookCount){for(LONG i=0;i<hookCount;i++)if(hooks[i].replacement==hookCreate)return patch(hooks[i].slot,hookCreate)?0:112;}
 unsigned char*base=(unsigned char*)host;IMAGE_DOS_HEADER*dos=(void*)base;IMAGE_NT_HEADERS64*nt=(void*)(base+dos->e_lfanew);IMAGE_IMPORT_DESCRIPTOR*imp=(void*)(base+nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
 for(;imp->Name;imp++){if(!imp->OriginalFirstThunk)continue;IMAGE_THUNK_DATA64*names=(void*)(base+imp->OriginalFirstThunk);IMAGE_THUNK_DATA64*iat=(void*)(base+imp->FirstThunk);for(;names->u1.AddressOfData;names++,iat++){if(IMAGE_SNAP_BY_ORDINAL64(names->u1.Ordinal))continue;IMAGE_IMPORT_BY_NAME*n=(void*)(base+names->u1.AddressOfData);if(!strcmp((char*)n->Name,"CoCreateInstance")){if(patch((void**)&iat->u1.Function,hookCreate))return 0;}}}
 status->active=0;return 106;
}
API DWORD BridgeStart(void*argument){
 uint64_t ids=(uint64_t)(uintptr_t)argument;unsigned gamePid=(unsigned)ids,inputPid=(unsigned)(ids>>32);HMODULE host=GetModuleHandleW(NULL);if(!host||!gamePid)return 101;
 DWORD bound=bindSource(gamePid,inputPid);return bound?bound:startBridge(host);
}
typedef struct {uint32_t producerPid;WCHAR mapping[128];} SourceRequest;
API DWORD BridgeStartSource(void*argument){
 SourceRequest*r=argument;HMODULE host=GetModuleHandleW(NULL);
 if(!host||!r||!r->producerPid||r->mapping[127]||wcsncmp(r->mapping,L"Local\\",6))return 101;
 DWORD bound=bindNamedSource(r->producerPid,r->producerPid,r->mapping);return bound?bound:startBridge(host);
}
API DWORD BridgeStop(void*unused){(void)unused;AcquireSRWLockExclusive(&transportLock);if(status)status->active=0;ReleaseSRWLockExclusive(&transportLock);restore();if(pumpThread){SetEvent(pumpStop);WaitForSingleObject(pumpThread,1000);CloseHandle(pumpThread);pumpThread=NULL;}AcquireSRWLockExclusive(&transportLock);if(eventCopy)CloseHandle(eventCopy);eventCopy=NULL;eventClient=NULL;clockCount=0;memset(clocks,0,sizeof(clocks));ReleaseSRWLockExclusive(&transportLock);return 0;}
API DWORD BridgeSizes(void*out){uint32_t*s=out;s[0]=sizeof(Source);s[1]=sizeof(Status);return 0;}
static float mockOriginal[960];
static HRESULT STDMETHODCALLTYPE mockBuffer(IAudioCaptureClient*self,BYTE**data,UINT32*frames,DWORD*flags,UINT64*d,UINT64*q){(void)self;(void)d;(void)q;*data=(BYTE*)mockOriginal;*frames=480;*flags=0;return S_OK;}
API DWORD BridgeSelfTest(void*unused){
 (void)unused;if(source||status||hookCount)return 1;source=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(Source));status=HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,sizeof(Status));if(!source||!status)return 2;
 LARGE_INTEGER f;QueryPerformanceFrequency(&f);source->frequency=f.QuadPart;source->active=1;source->streams[0].writes=1;Block*b=&source->streams[0].blocks[0];b->sequence=2;b->frames=2048;b->channels=2;b->tick=now();for(unsigned i=0;i<2048;i++){b->pcm[i]=.25f;b->pcm[2048+i]=.125f;}
 status->active=1;status->gain=2;status->rate=48000;status->channels=2;status->bits=32;status->controllerTick=now()+source->frequency;sourcePosition=blockOffset=blockFrames=0;
 void*methods[6]={0,0,0,mockBuffer,0,0};void**object=methods;trackClock(&object);clocks[0].next=now()-source->frequency/100;
 BYTE*data;UINT32 frames;DWORD flags;HRESULT hr=hookBuffer((IAudioCaptureClient*)&object,&data,&frames,&flags,NULL,NULL);int ok=hr==S_OK&&frames==480&&data==(BYTE*)output&&output[0]==.5f&&output[1]==.25f&&status->copiedBlocks==1&&status->inputPeak==.25f;
 ok=ok&&hookRelease((IAudioCaptureClient*)&object,frames)==S_OK;clocks[0].next=now()-source->frequency/100;status->controllerTick=now()-source->frequency*6;hr=hookBuffer((IAudioCaptureClient*)&object,&data,&frames,&flags,NULL,NULL);ok=ok&&hr==S_OK&&data==(BYTE*)output&&output[0]==0&&!status->active&&source->active&&(flags&AUDCLNT_BUFFERFLAGS_SILENT);
 hookRelease((IAudioCaptureClient*)&object,frames);HeapFree(GetProcessHeap(),0,source);HeapFree(GetProcessHeap(),0,status);source=NULL;status=NULL;clockCount=0;memset(clocks,0,sizeof(clocks));return ok?0:4;
}
API DWORD BridgeRebindSelfTest(void*unused){
 (void)unused;if(source||status)return 1;HANDLE maps[2]={0};Source*views[2]={0};DWORD result=0;
 for(unsigned i=0;i<2;i++){unsigned id=0x7f000000u+GetCurrentProcessId()+i;WCHAR name[64];mappingName(name,L"Local\\CustomAdapterMix-",id);maps[i]=CreateFileMappingW(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,sizeof(Source),name);if(!maps[i]){result=2;goto done;}views[i]=MapViewOfFile(maps[i],FILE_MAP_ALL_ACCESS,0,0,sizeof(Source));if(!views[i]){result=3;goto done;}memset(views[i],0,sizeof(Source));views[i]->magic=0x37504341;views[i]->version=1;views[i]->pid=id;views[i]->active=1;views[i]->frequency=10000000;views[i]->streams[0].rate=48000;views[i]->streams[0].writes=3+i;
  DWORD r=bindNamedSource(100+i,id,name);if(r||status->gamePid!=100+i||source->pid!=id||sourcePosition!=3+i){result=4;goto done;}
 }
 if(bindSource(999,0x6fffffff)==0||status->gamePid!=101)result=5;
 done:if(source)UnmapViewOfFile(source);if(sourceMap)CloseHandle(sourceMap);if(status)UnmapViewOfFile(status);if(statusMap)CloseHandle(statusMap);source=NULL;status=NULL;sourceMap=statusMap=NULL;for(unsigned i=0;i<2;i++){if(views[i])UnmapViewOfFile(views[i]);if(maps[i])CloseHandle(maps[i]);}return result;
}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID reserved){(void)reserved;if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(instance);return TRUE;}
