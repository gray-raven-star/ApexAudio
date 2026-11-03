using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApexAudio.Contracts;
using ApexAudio.Core;
using ApexAudio.Fmod.Definition;

namespace ApexAudio.Authoring;

public sealed class AuthoringValidationException(IReadOnlyList<ConfigurationDiagnostic> diagnostics) : IOException("配置定义未通过静态校验。")
{
    public IReadOnlyList<ConfigurationDiagnostic> Diagnostics { get; }=diagnostics;
}
public static class GamepackAuthoring
{
    public static BackendDescription Describe(string directory)
    {
        var descriptor=ReadBackend(directory);string variant=descriptor.Variants.Keys.First();
        BackendFiles.Read(directory,new(descriptor.Id,descriptor.Version,BackendFiles.Hash(BackendFiles.PathIn(directory,"backend.json")),variant));
        return new(descriptor.Id,descriptor.Version,variant,descriptor.ContextVersion,descriptor.Support);
    }
    static BackendDescriptor ReadBackend(string directory)
    {
        var descriptor=JsonSerializer.Deserialize<BackendDescriptor>(File.ReadAllText(BackendFiles.PathIn(directory,"backend.json")),Wire.Json)
            ?? throw new InvalidDataException("后端描述为空。");
        if(string.IsNullOrWhiteSpace(descriptor.Id) || string.IsNullOrWhiteSpace(descriptor.Version) || descriptor.Files==null || descriptor.Variants==null || descriptor.Variants.Count==0)
            throw new InvalidDataException("后端描述缺少身份、文件或变体。");
        return descriptor;
    }
    public static void Create(string directory,string backend,string id="community.example",string name="TODO 游戏名称",string version="0.1.0",string variant="studio-v1")
    {
        directory=Path.GetFullPath(directory);
        if(Directory.Exists(directory) || File.Exists(directory))throw new IOException("定义目录已存在，请选择新目录。");
        if(!Regex.IsMatch(id,"^[a-z0-9][a-z0-9.-]{2,79}$") || !Version.TryParse(version,out _))throw new InvalidDataException("包 ID 或版本无效。");
        var descriptor=ReadBackend(backend);
        var dependency=new BackendDependency(descriptor.Id,descriptor.Version,BackendFiles.Hash(BackendFiles.PathIn(backend,"backend.json")),variant);
        BackendFiles.Read(backend,dependency);RequireKnownSupport(descriptor,dependency);
        string parent=Path.GetDirectoryName(directory)!;Directory.CreateDirectory(parent);BackendFiles.PathIn(parent,Path.GetFileName(directory)+"/manifest.json");
        Directory.CreateDirectory(directory);
        try
        {
            Directory.CreateDirectory(Path.Combine(directory,"profiles"));Directory.CreateDirectory(Path.Combine(directory,"presets"));
            var manifest=new
            {
                schemaVersion=2,id,name,version,author="TODO 作者",source="TODO 来源",description="JSON 共享 FMOD 后端配置",protocolVersion=Wire.Version,minHostVersion=V2Packages.HostVersion,
                games=new[]{new GameMatch("TODO-Game.exe","TODO_EXE_SHA256","default",new(){{"fmod.dll","TODO_FMOD_SHA256"},{"fmodstudio.dll","TODO_STUDIO_SHA256"},{"TODO-GameAudio.dll","TODO_WRAPPER_SHA256"}})},
                profiles=new[]{new CaptureProfile("default",dependency,"profiles/default.json")},presets=new[]{"presets/default.json"},files=new Dictionary<string,string>()
            };
            Write("manifest.json",manifest);
            Write("profiles/default.json",new{schemaVersion=2,name,locator="wrapper",wrapperModule="TODO-GameAudio.dll",wrapperRva=0,runtimeVersion=0,stateOffset=0,studioOffset=8,
                buses=new[]{new{id="TODO_32_HEX_FMOD_GUID",path="TODO bus path",name="TODO 声音组",category="other",containsChildren=false,stableId="audio.example"}}});
            Write("presets/default.json",new SoundPreset{Id="author-default",Name="初始预设",PackId=id,Groups=new(){{"audio.example",new(Enabled:false)}}});
            File.WriteAllText(Path.Combine(directory,"README.txt"),"这是未完成的配置起点。所有 TODO、零 RVA 和零运行时版本都必须替换为实际调查证据。\nGUID 与摘要不得猜测；稳定 ID 表示声音语义。新增组默认不参与反馈。\nfiles 由工具生成；validate/package 均需 --backend 指定本地固定后端目录。\n静态校验不连接游戏，不代表实机兼容或采集成功。实机覆盖另写包说明。\n");
        }
        catch{Directory.Delete(directory,true);throw;}
        void Write(string relative,object value)=>File.WriteAllText(BackendFiles.PathIn(directory,relative),JsonSerializer.Serialize(value,Wire.Json));
    }
    public static IReadOnlyList<ConfigurationDiagnostic> Validate(string input,IReadOnlyList<string> backends)
    {
        string stage=NewStage();
        try{BuildStage(input,stage,backends);return Array.Empty<ConfigurationDiagnostic>();}
        catch(AuthoringValidationException e){return e.Diagnostics;}
        catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return new[]{Diagnostic(e,"manifest.json")};}
        finally{Directory.Delete(stage,true);}
    }
    public static PackageVersionRef Package(string input,string output,IReadOnlyList<string> backends)
    {
        input=Path.GetFullPath(input);output=Path.GetFullPath(output);
        if(File.Exists(output) || Directory.Exists(output))throw new IOException("输出已存在，请使用新文件名。");
        if(output.StartsWith(input.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("输出必须在定义目录以外。");
        string stage=NewStage(),temporary=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var manifest=BuildStage(input,stage,backends);var reference=new PackageVersionRef(manifest.Id,manifest.Version,PackageStore.Hash(Path.Combine(stage,"manifest.json")));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);ZipFile.CreateFromDirectory(stage,temporary);
            string check=NewStage();
            try{new PackageStore(check).Inspect(temporary);}finally{Directory.Delete(check,true);}
            File.Move(temporary,output);return reference;
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);Directory.Delete(stage,true);}
    }
    static string NewStage()
    {
        string root=Environment.GetEnvironmentVariable("APEX_AUDIO_TEMP") ?? Path.GetTempPath();
        string stage=Path.Combine(root,"ApexAudio-PackV2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);return stage;
    }
    static PackManifest BuildStage(string input,string stage,IReadOnlyList<string> backends)
    {
        input=Path.GetFullPath(input);string manifestFile=BackendFiles.PathIn(input,"manifest.json");
        if(new FileInfo(manifestFile).Length>4194304)throw new InvalidDataException("manifest.json 超过 4 MiB。");
        var doc=JsonNode.Parse(File.ReadAllText(manifestFile)) as JsonObject ?? throw new InvalidDataException("清单必须是对象。");
        var files=new JsonObject();int count=0;long bytes=0;Copy(input);doc["files"]=files;
        File.WriteAllText(Path.Combine(stage,"manifest.json"),doc.ToJsonString(Wire.Json));
        var manifest=PackageStore.ReadManifest(Path.Combine(stage,"manifest.json"));
        if(manifest.SchemaVersion!=2)throw new InvalidDataException("此工具只制作 JSON V2 配置包。");
        var errors=new List<ConfigurationDiagnostic>();Placeholders(doc,"$","manifest.json");
        var releases=new List<(string Root,BackendDescriptor Descriptor)>();
        foreach(string folder in backends)
        {
            try{releases.Add((Path.GetFullPath(folder),ReadBackend(folder)));}
            catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException){errors.Add(Diagnostic(e,"backend.json"));}
        }
        var availableGroups=new HashSet<string>();
        for(int i=0;i<manifest.Profiles.Count;i++)
        {
            var profile=manifest.Profiles[i];string at=$"$.profiles[{i}]";
            try
            {
                var available=releases.Where(r=>r.Descriptor.Id==profile.Backend.Id && r.Descriptor.Version==profile.Backend.Version).ToArray();
                if(available.Length!=1)throw new OperationException("backend_missing","请用 --backend 唯一指定该 Profile 固定的本地后端目录。","provide_backend",new Dictionary<string,string>{{"field",at+".backend"}});
                var release=available[0];BackendFiles.Read(release.Root,profile.Backend);RequireKnownSupport(release.Descriptor,profile.Backend);
                string file=BackendFiles.PathIn(stage,profile.Parameters);
                if(new FileInfo(file).Length>FmodParameterContract.MaxBytes)throw new InvalidDataException("参数文件超过 1 MiB。");
                var json=JsonNode.Parse(File.ReadAllText(file))!;Placeholders(json,"$",profile.Parameters);
                var parameterErrors=new FmodConfigurationValidator().Validate(release.Descriptor,profile,Wire.Element(json),manifest.Games,profile.Parameters);errors.AddRange(parameterErrors);
                if(parameterErrors.Count==0)
                {
                    var value=json.Deserialize<FmodParameters>(Wire.Json)!;
                    foreach(var bus in value.Buses)availableGroups.Add(bus.StableId ?? bus.Id);
                }
            }
            catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException)
            {errors.Add(Diagnostic(e,profile.Parameters,at));}
        }
        foreach(string preset in manifest.Presets)
        {
            try
            {
                var value=JsonSerializer.Deserialize<SoundPreset>(File.ReadAllText(BackendFiles.PathIn(stage,preset)),Wire.Json)!;
                PresetStore.Validate(value,manifest.Id);
                foreach(string group in value.Groups.Keys)
                    if(!availableGroups.Contains(group))errors.Add(new(preset,"$.groups."+group,"invalid_reference","作者预设引用的声音组未在任何有效 Profile 中定义。"));
            }
            catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException){errors.Add(Diagnostic(e,preset));}
        }
        try{PackageStore.Validate(stage,manifest);}catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException){errors.Add(Diagnostic(e,"manifest.json"));}
        if(errors.Count!=0)throw new AuthoringValidationException(errors);
        return manifest;
        void Copy(string directory)
        {
            foreach(string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                string relative=Path.GetRelativePath(input,entry).Replace('\\','/');string source=BackendFiles.PathIn(input,relative);
                if(++count>10000)throw new InvalidDataException("定义目录超过 10000 项。");
                if(Directory.Exists(source)){Copy(source);continue;}
                if(relative.Equals("manifest.json",StringComparison.OrdinalIgnoreCase))continue;
                bytes+=new FileInfo(source).Length;if(bytes>1073741824)throw new InvalidDataException("定义目录超过 1 GiB。");
                string target=BackendFiles.PathIn(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target);files[relative]=PackageStore.Hash(target);
            }
        }
        void Placeholders(JsonNode? node,string path,string file)
        {
            if(node is JsonObject obj){foreach(var item in obj)Placeholders(item.Value,path+"."+item.Key,file);}
            else if(node is JsonArray array){for(int i=0;i<array.Count;i++)Placeholders(array[i],$"{path}[{i}]",file);}
            else if(node is JsonValue value && value.TryGetValue<string>(out string? text) && text.StartsWith("TODO",StringComparison.OrdinalIgnoreCase))errors.Add(new(file,path,"placeholder","请填写实际调查结果，不能发布模板占位项。"));
        }
    }
    static void RequireKnownSupport(BackendDescriptor descriptor,BackendDependency dependency)
    {
        if(descriptor.Id!="apex.fmod" || descriptor.Version!="0.3.0" || descriptor.ContextVersion!=2 || dependency.Variant!="studio-v1" ||
            descriptor.Support?.Parameters.TryGetValue(dependency.Variant,out var support)!=true || support==null || !FmodParameterContract.MatchesSupport(support))
            throw new OperationException("backend_unsupported","此作者工具仅支持已声明的 FMOD 参数格式 2 / studio-v1；旧包保持原安装运行流程。","choose_backend_version");
    }
    static ConfigurationDiagnostic Diagnostic(Exception error,string file,string path="$")
    {
        var operation=(error as OperationException)?.Error;
        return new(file,operation?.Details?.GetValueOrDefault("field") ?? (error as JsonException)?.Path ?? path,operation?.Code ?? "invalid_definition",error.Message);
    }
}
