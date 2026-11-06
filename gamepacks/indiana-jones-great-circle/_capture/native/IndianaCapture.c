/* Checksum-bound package-side resolver for the examined Indiana executable.
 * Reuses the unchanged Wwise PCM/recording ABI. This is not a universal RVA map.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
static unsigned char*image(void){return (unsigned char*)GetModuleHandleW(NULL);}
static int isInitialized(void){return image()[0x4a47f6f]!=0;}
static int getSettings(uint32_t*out){
    if(!isInitialized()||!out)return 102;
    out[0]=*(uint16_t*)(image()+0x3ee1494);
    out[1]=*(uint32_t*)(image()+0x3ee166c);
    return 1;
}
static int registerPlugin(int type,uint32_t company,uint32_t id,void*fx,void*params,void*device){
    int(*fn)(int,uint32_t,uint32_t,void*,void*,void*)=(void*)(image()+0x219a5a0);
    return fn(type,company,id,fx,params,device);
}
typedef void(*RenderCallback)(void*,uint32_t,void*);
static int addCallback(RenderCallback cb,uint32_t locations,void*cookie,int type,uint32_t company,uint32_t id){
    (void)type;(void)company;(void)id;
    int(*fn)(RenderCallback,uint32_t,void*)=(void*)(image()+0x2172f00);
    return fn(cb,locations,cookie);
}
static int removeCallback(RenderCallback cb,uint32_t locations){
    int(*fn)(RenderCallback,uint32_t)=(void*)(image()+0x2173070);
    return fn(cb,locations);
}
static int queueEffect(uint32_t bus,uint32_t slot,uint32_t fx){
    if(!isInitialized()||slot>3)return 31;
    unsigned char*mgr=*(unsigned char**)(image()+0x4a48098);
    if(!mgr)return 102;
    uint32_t*(*enqueue)(void*,uint32_t,uint32_t)=(void*)(image()+0x2175760);
    uint32_t*msg=enqueue(mgr,0x2c,20);
    if(!msg)return 81;
    msg[1]=bus;msg[2]=slot;msg[3]=fx;msg[4]=1;
    InterlockedDecrement((volatile LONG*)(mgr+0xb4));
    return 1;
}
typedef struct{
    uint32_t command,type,id,reserved;
    const char*name;
    uint8_t freeName,pad[7];
    void*callback;void*cookie;
    uint32_t source,pad2;
    const void*bytes;uint32_t size,pad3;
    uint64_t pad4;
} BankRequest;
_Static_assert(sizeof(BankRequest)==80,"bank request ABI");
static volatile LONG bankComplete;
static int bankStatus;
static BankRequest pendingBank;
static void*pendingBytes;
static int bankRequested;
static void bankDone(uint32_t id,const void*data,int result,void*cookie){
    (void)id;(void)data;(void)cookie;bankStatus=result;InterlockedExchange(&bankComplete,1);
}
static int loadMemory(const void*bytes,uint32_t size,uint32_t*out){
    if(!isInitialized()||!bytes||size<20||memcmp(bytes,"BKHD",4))return 31;
    void*mgr=*(void**)(image()+0x4a48090);
    if(!mgr)return 102;
    if(!bankRequested){
        pendingBytes=HeapAlloc(GetProcessHeap(),0,size);
        if(!pendingBytes)return 52;
        memcpy(pendingBytes,bytes,size);
        memset(&pendingBank,0,sizeof(pendingBank));
        pendingBank.id=*(const uint32_t*)((const char*)bytes+12);
        pendingBank.callback=bankDone;pendingBank.cookie=&bankStatus;
        pendingBank.source=3;pendingBank.bytes=pendingBytes;pendingBank.size=size;
        bankComplete=0;bankStatus=2;
        int(*queue)(void*,const BankRequest*)=(void*)(*(void***)mgr)[3];
        int result=queue(mgr,&pendingBank);
        if(result!=1){HeapFree(GetProcessHeap(),0,pendingBytes);pendingBytes=NULL;return result;}
        bankRequested=1;
    }
    // The runtime object is a threaded subclass. Both the request and its byte
    // copy have module lifetime until completion; a timeout must not free them.
    ULONGLONG deadline=GetTickCount64()+2000;
    while(!InterlockedCompareExchange(&bankComplete,0,0)&&GetTickCount64()<deadline)Sleep(1);
    if(!InterlockedCompareExchange(&bankComplete,0,0))return 77;
    if(pendingBytes){HeapFree(GetProcessHeap(),0,pendingBytes);pendingBytes=NULL;}
    if(out)*out=pendingBank.id;
    return bankStatus;
}
static FARPROC resolveIndiana(HMODULE module,LPCSTR name){
    FARPROC actual=GetProcAddress(module,name);
    if(actual||module!=(HMODULE)image())return actual;
    if(!memcmp(name,"?IsInitialized@",15))return (FARPROC)isInitialized;
    if(!memcmp(name,"?GetAudioSettings@",18))return (FARPROC)getSettings;
    if(!memcmp(name,"?RegisterPlugin@",16))return (FARPROC)registerPlugin;
    if(!memcmp(name,"?LoadBankMemoryCopy@",20))return (FARPROC)loadMemory;
    if(!memcmp(name,"?SetBusEffect@",14))return (FARPROC)queueEffect;
    if(!memcmp(name,"?RegisterGlobalCallback@",24))return (FARPROC)addCallback;
    if(!memcmp(name,"?UnregisterGlobalCallback@",26))return (FARPROC)removeCallback;
    return NULL;
}
#define GetProcAddress resolveIndiana
#define CAPTURE_BANK_VERSION 150
#define CAPTURE_SDK_YEAR 2023
#define CAPTURE_SDK_SERIES 1
#include "../../../../adapters/Wwise/native/ApexWwiseCapture.c"
