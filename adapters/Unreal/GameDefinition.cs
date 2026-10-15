using System.Text;
using System.Text.Json;
using ApexAudio.Contracts;
namespace ApexAudio.Unreal;
sealed class GameDefinition
{
 public string Name{get;set;}="";public string Executable{get;set;}="";public string ExecutableSha256{get;set;}="";
 public string CaptureFile{get;set;}="";public string CaptureSha256{get;set;}="";
 public ulong EngineRva{get;set;}public ulong NameBlocksRva{get;set;}public ulong VtableRva{get;set;}public ulong RegisterRva{get;set;}public ulong UnregisterRva{get;set;}
 public uint HolderOffset{get;set;}public uint DeviceOffset{get;set;}
 public List<SubmixDefinition>Buses{get;set;}=new();
 public static GameDefinition Load()=>JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"game.json")),Wire.Json)!;
 public byte[] Configuration()
 {
  if(Buses.Count is <1 or >64||Buses.Select(b=>b.Id).Distinct().Count()!=Buses.Count)throw new IOException("Submix 分组配置无效。");
  using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
  writer.Write(0x43554141u);writer.Write(1u);
  foreach(ulong value in new[]{EngineRva,NameBlocksRva,VtableRva,RegisterRva,UnregisterRva})writer.Write(value);
  writer.Write(HolderOffset);writer.Write(DeviceOffset);writer.Write((uint)Buses.Count);writer.Write(0u);
  for(int i=0;i<64;i++){byte[] name=i<Buses.Count?Encoding.Unicode.GetBytes(Buses[i].NativeName):Array.Empty<byte>();if(name.Length>190)throw new IOException("Submix 名称过长。");writer.Write(name);writer.Write(new byte[192-name.Length]);}
  return stream.ToArray();
 }
}
sealed class SubmixDefinition
{
 public string Id{get;set;}="";public string Name{get;set;}="";public string NativeName{get;set;}="";
 public string?ParentId{get;set;}public string Category{get;set;}="其他";public bool ContainsChildren{get;set;}
}
