using System.IO.Compression;
using System.Text.Json.Nodes;
using ApexAudio.Contracts;
using ApexAudio.Core;

if(args.Length!=2){Console.WriteLine("ApexAudio.PackV2 <definition-directory> <output.gpack>\nRead JSON configuration, regenerate integrity hashes and package it; no game or backend code is executed.");return 1;}
string input=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
if(File.Exists(output))throw new IOException("输出已存在，请使用新的文件名。");
if(output.StartsWith(input.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("输出必须在定义目录以外。");
string temporary=Path.Combine(Environment.GetEnvironmentVariable("APEX_AUDIO_TEMP")??Path.GetTempPath(),"ApexAudio-PackV2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
try
{
    var doc=JsonNode.Parse(File.ReadAllText(Path.Combine(input,"manifest.json")))!.AsObject();
    if(doc["schemaVersion"]?.GetValue<int>()!=2)throw new InvalidDataException("此工具仅打包 V2 配置。");
    var files=new JsonObject();
    foreach(string file in Directory.EnumerateFiles(input,"*",SearchOption.AllDirectories))
    {
        string relative=Path.GetRelativePath(input,file).Replace('\\','/');if(relative=="manifest.json")continue;
        string verified=BackendFiles.PathIn(input,relative),dest=BackendFiles.PathIn(temporary,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(verified,dest);files[relative]=PackageStore.Hash(dest);
    }
    doc["files"]=files;File.WriteAllText(Path.Combine(temporary,"manifest.json"),doc.ToJsonString(Wire.Json));
    var manifest=PackageStore.ReadManifest(Path.Combine(temporary,"manifest.json"));PackageStore.Validate(temporary,manifest);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);ZipFile.CreateFromDirectory(temporary,output);
    Console.WriteLine(manifest.Name+" "+manifest.Version+"\n"+output+"\nSHA256 "+PackageStore.Hash(output));return 0;
}
finally{Directory.Delete(temporary,true);}
