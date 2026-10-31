/* Exercises the same version-call helper used by capture, in our own process. */
#include "../adapters/Fmod/native/ApexFmodCapture.c"
#include <stdio.h>
int main(int argc,char**argv){
 if(argc!=4)return 1;
 HMODULE c=LoadLibraryA(argv[1]),s=LoadLibraryA(argv[2]);if(!c||!s)return 2;
 cfg.runtimeVersion=strtoul(argv[3],0,0);
 int(*create)(void**,unsigned)=(void*)GetProcAddress(s,"FMOD_Studio_System_Create");
 int(*release)(void*)=(void*)GetProcAddress(s,"FMOD_Studio_System_Release");
 getCore=(void*)GetProcAddress(s,"FMOD_Studio_System_GetCoreSystem");getVersion=(void*)GetProcAddress(c,"FMOD_System_GetVersion");
 void*system=0,*cs=0;unsigned version=0;
 if(!create||!release||!getCore||!getVersion||create(&system,cfg.runtimeVersion))return 3;
 int code=getCore(system,&cs);if(!code)code=runtimeVersion(cs,&version);
 printf("result=%d expected=%x actual=%x\n",code,cfg.runtimeVersion,version);
 release(system);return code||version!=cfg.runtimeVersion;
}
