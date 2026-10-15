using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ApexAudio.Windows;
public static class Native
{
 [DllImport("kernel32")] public static extern ulong GetTickCount64();
 [DllImport("kernel32",SetLastError=true)] public static extern nint OpenProcess(uint rights,bool inherit,int pid);
 [DllImport("kernel32",SetLastError=true)] public static extern bool CloseHandle(nint handle);
 [DllImport("kernel32",SetLastError=true)] public static extern nint VirtualAllocEx(nint process,nint address,nuint size,uint kind,uint protection);
 [DllImport("kernel32",SetLastError=true)] public static extern bool VirtualFreeEx(nint process,nint address,nuint size,uint kind);
 [DllImport("kernel32",SetLastError=true)] public static extern bool WriteProcessMemory(nint process,nint address,byte[] data,nuint size,out nuint written);
 [DllImport("kernel32",SetLastError=true)] public static extern bool FlushInstructionCache(nint process,nint address,nuint size);
 [DllImport("kernel32",SetLastError=true)] public static extern nint CreateRemoteThread(nint process,nint attributes,nuint stack,nint entry,nint arg,uint flags,nint id);
 [DllImport("kernel32",SetLastError=true)] public static extern uint WaitForSingleObject(nint handle,uint milliseconds);
 [DllImport("kernel32",SetLastError=true)] public static extern bool GetExitCodeThread(nint thread,out uint code);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] public static extern nint GetModuleHandle(string module);
 [DllImport("kernel32",CharSet=CharSet.Ansi)] public static extern nint GetProcAddress(nint module,string entry);
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] public static extern bool GetModuleHandleEx(uint flags,nint address,out nint module);
 [DllImport("kernel32",CharSet=CharSet.Unicode)] public static extern uint GetModuleFileName(nint module,StringBuilder name,int size);
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)] public static extern nint OpenFileMapping(uint rights,bool inherit,string name);
 [DllImport("kernel32",SetLastError=true)] public static extern nint MapViewOfFile(nint handle,uint rights,uint high,uint low,nuint bytes);
 [DllImport("kernel32")] public static extern bool UnmapViewOfFile(nint view);
 public static Exception Error(string message)=>new IOException(message,new Win32Exception(Marshal.GetLastWin32Error()));
 public static string Hash(string path){using var f=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(f));}
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] public delegate uint Test(nint arg);
}
public sealed class Target:IDisposable
{
 sealed class PendingCallException:IOException { public PendingCallException():base("音频接口调用超时，已保留正在运行的临时调用。请重启对应应用。"){} }
 public Process Process {get;}
 readonly nint handle;
 public Target(Process process,string expectedName,string expectedHash)
 {
  Process=process;
  string path=process.MainModule?.FileName??throw new IOException("无法读取应用路径。当前进程权限不足。");
  if(!Path.GetFileName(path).Equals(expectedName,StringComparison.OrdinalIgnoreCase))throw new IOException("应用路径与预期不符。");
  if(!Native.Hash(path).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new IOException("应用版本与适配信息不匹配，请更新适配包或反馈模块。");
  handle=Native.OpenProcess(0x43a,false,process.Id);if(handle==0)throw Native.Error("无法连接应用。当前进程权限不足。");
 }
 nint Allocate(int size,bool executable=false){nint p=Native.VirtualAllocEx(handle,0,(nuint)size,0x3000,executable?0x40u:4u);if(p==0)throw Native.Error("无法准备临时调用。");return p;}
 void Write(nint address,byte[] data){if(!Native.WriteProcessMemory(handle,address,data,(nuint)data.Length,out var n)||n!=(nuint)data.Length)throw Native.Error("无法写入临时调用。");}
 public uint CallData(nint address,byte[] data){nint block=Allocate(data.Length);bool pending=false;try{Write(block,data);return Call(address,(ulong)block);}catch(PendingCallException){pending=true;throw;}finally{if(!pending)Native.VirtualFreeEx(handle,block,0,0x8000);}}
 public uint Call(nint address,ulong arg=0,bool pointerResult=false)
 {
  var code=new List<byte>{0x48,0x83,0xec,0x28,0x48,0xb9};code.AddRange(BitConverter.GetBytes(arg));code.AddRange(new byte[]{0x48,0xb8});code.AddRange(BitConverter.GetBytes((ulong)address));code.AddRange(new byte[]{0xff,0xd0,0x48,0x83,0xc4,0x28,0xc3});
  nint block=Allocate(code.Count,true),thread=0;bool running=false;
  try {Write(block,code.ToArray());Native.FlushInstructionCache(handle,block,(nuint)code.Count);thread=Native.CreateRemoteThread(handle,0,0,block,0,0,0);if(thread==0)throw Native.Error("无法执行临时调用。");running=true;uint wait=Native.WaitForSingleObject(thread,5000);if(wait!=0)throw new PendingCallException();running=false;if(!Native.GetExitCodeThread(thread,out var result))throw Native.Error("无法读取调用结果。");if(!pointerResult&&result>=0xc0000000)throw new IOException($"音频接口异常：{result:X8}");return result;}
  finally {if(thread!=0)Native.CloseHandle(thread);if(!running)Native.VirtualFreeEx(handle,block,0,0x8000);}
 }
 ProcessModule? Module(string name)=>Process.Modules.Cast<ProcessModule>().SingleOrDefault(m=>m.ModuleName.Equals(name,StringComparison.OrdinalIgnoreCase));
 public nint Load(string path)
 {
  string name=Path.GetFileName(path);var found=Module(name);
  if(found==null)
  {
   nint localLoad=Native.GetProcAddress(Native.GetModuleHandle("kernel32.dll"),"LoadLibraryW");if(!Native.GetModuleHandleEx(6,localLoad,out var owner))throw Native.Error("无法确定系统加载接口。");var file=new StringBuilder(32768);Native.GetModuleFileName(owner,file,file.Capacity);var targetOwner=Module(Path.GetFileName(file.ToString()))??throw new IOException("系统加载模块不可用。");nint remoteLoad=targetOwner.BaseAddress+(localLoad-owner);
   byte[] text=Encoding.Unicode.GetBytes(Path.GetFullPath(path)+"\0");nint buffer=Allocate(text.Length);
   bool pending=false;
   try {Write(buffer,text);Call(remoteLoad,(ulong)buffer,true);}catch(PendingCallException){pending=true;throw;}finally{if(!pending)Native.VirtualFreeEx(handle,buffer,0,0x8000);}
   Process.Refresh();found=Module(name);
  }
  if(found==null||!Native.Hash(found.FileName).Equals(Native.Hash(path),StringComparison.OrdinalIgnoreCase))throw new IOException("发现不同版本的采集模块，请重新打开游戏和 X-Haptics。");
  return found.BaseAddress;
 }
 public nint Export(string dll,nint remoteBase,string entry)
 {
  nint local=NativeLibrary.Load(dll);
  try{return remoteBase+(NativeLibrary.GetExport(local,entry)-local);}finally{NativeLibrary.Free(local);}
 }
 public void Dispose()=>Native.CloseHandle(handle);
}

