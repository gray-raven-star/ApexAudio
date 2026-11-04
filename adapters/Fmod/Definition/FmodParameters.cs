using System.Text.Json;
using System.Text.RegularExpressions;
using ApexAudio.Contracts;

namespace ApexAudio.Fmod.Definition;

public class FmodParameters
{
    public int SchemaVersion { get; set; }=1;
    public string Name { get; set; }="";
    public string WrapperModule { get; set; }="";
    public uint WrapperRva { get; set; }
    public uint RuntimeVersion { get; set; }
    public uint StateOffset { get; set; }
    public uint StudioOffset { get; set; }
    public string Locator { get; set; }="wrapper";
    public uint StudioHandle { get; set; }
    public List<FmodBus> Buses { get; set; }=new();
}
public sealed class FmodBus
{
    public string Id { get; set; }="";
    public string Path { get; set; }="";
    public string Name { get; set; }="";
    public string? ParentId { get; set; }
    public string Category { get; set; }="other";
    public bool ContainsChildren { get; set; }
    public string? StableId { get; set; }
}

public static class FmodParameterContract
{
    public const string Format="apex.fmod.parameters";
    public const int Version=2,MaxBuses=64,MaxBytes=1048576,MaxModuleCharacters=127;
    public const uint MinimumRuntime=0x20200,MaximumRuntime=0x203ff;
    static readonly string[] Modes=["wrapper","studioHandle"];
    static readonly Dictionary<string,JsonValueKind> Fields=new(StringComparer.OrdinalIgnoreCase)
    {
        ["schemaVersion"]=JsonValueKind.Number,["name"]=JsonValueKind.String,["wrapperModule"]=JsonValueKind.String,
        ["wrapperRva"]=JsonValueKind.Number,["runtimeVersion"]=JsonValueKind.Number,["stateOffset"]=JsonValueKind.Number,
        ["studioOffset"]=JsonValueKind.Number,["locator"]=JsonValueKind.String,["studioHandle"]=JsonValueKind.Number,["buses"]=JsonValueKind.Array
    };
    static readonly Dictionary<string,JsonValueKind> BusFields=new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"]=JsonValueKind.String,["path"]=JsonValueKind.String,["name"]=JsonValueKind.String,["parentId"]=JsonValueKind.String,
        ["category"]=JsonValueKind.String,["containsChildren"]=JsonValueKind.True,["stableId"]=JsonValueKind.String
    };
    public static BackendSupport Support=>new("windows-x64",Wire.Version,new()
    {
        ["studio-v1"]=new(Format,Version,MaxBytes,Wire.Element(new
        {
            locators=Modes,maximumBuses=MaxBuses,minimumRuntimeVersion=MinimumRuntime,maximumRuntimeVersion=MaximumRuntime,
            maximumModuleCharacters=MaxModuleCharacters,maximumStateOffset=4092,maximumStudioOffset=4088,
            requiredModules=new[]{"fmod.dll","fmodstudio.dll","<wrapperModule>"},
            wrapperRequires=new[]{"wrapperRva > 0","studioHandle = 0"},studioHandleRequires=new[]{"studioHandle > 0","wrapperRva = stateOffset = studioOffset = 0"},unknownFields="rejected"
        }))
    });
    public static IReadOnlyList<ConfigurationDiagnostic> Check(JsonElement json,string file)
    {
        var errors=new List<ConfigurationDiagnostic>();
        if(System.Text.Encoding.UTF8.GetByteCount(json.GetRawText())>MaxBytes){Add("$","limit_exceeded","参数超过 1 MiB。");return errors;}
        Shape(json,"$",Fields,["schemaVersion","name","wrapperModule","runtimeVersion","buses"]);
        if(json.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in json.EnumerateObject().Where(p=>p.Name.Equals("buses",StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind==JsonValueKind.Array))
            {
                if(property.Value.GetArrayLength()>MaxBuses){Add("$.buses","limit_exceeded","最多支持 64 条总线。");continue;}
                int i=0;foreach(var bus in property.Value.EnumerateArray())Shape(bus,$"$.buses[{i++}]",BusFields,["id","name"]);
            }
        }
        if(errors.Count!=0)return errors;
        try
        {
            var value=json.Deserialize<FmodParameters>(Wire.Json)!;
            if(value.SchemaVersion!=Version)Add("$.schemaVersion","unsupported_format","FMOD 共享参数仅支持格式 2。");
            errors.AddRange(CheckValues(value,file));
        }
        catch(JsonException e){Add(e.Path ?? "$","invalid_type","字段类型或数值不受支持。");}
        return errors;
        void Add(string path,string code,string message)=>errors.Add(new(file,path,code,message));
        void Shape(JsonElement value,string path,Dictionary<string,JsonValueKind> fields,string[] required)
        {
            if(value.ValueKind!=JsonValueKind.Object){Add(path,"invalid_type","必须是 JSON 对象。");return;}
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var property in value.EnumerateObject())
            {
                string field=path+"."+property.Name;
                if(!seen.Add(property.Name)){Add(field,"duplicate_field","字段重复。");continue;}
                if(!fields.TryGetValue(property.Name,out var kind)){Add(field,"unsupported_field","此参数格式不支持该字段。");continue;}
                bool nullable=path!="$" && (property.Name.Equals("parentId",StringComparison.OrdinalIgnoreCase) || property.Name.Equals("stableId",StringComparison.OrdinalIgnoreCase));
                if(nullable && property.Value.ValueKind==JsonValueKind.Null)continue;
                if(property.Value.ValueKind!=kind && !(kind==JsonValueKind.True && property.Value.ValueKind==JsonValueKind.False))Add(field,"invalid_type","字段类型不正确。");
                else if(kind==JsonValueKind.Number && !property.Value.TryGetUInt32(out _))Add(field,"invalid_type","必须是 0～4294967295 的整数。");
            }
            foreach(string name in required)if(!seen.Contains(name))Add(path+"."+name,"required_field","缺少必需字段。");
        }
    }
    public static IReadOnlyList<ConfigurationDiagnostic> CheckValues(FmodParameters p,string file)
    {
        var errors=new List<ConfigurationDiagnostic>();
        void Add(string path,string message)=>errors.Add(new(file,path,"unsupported_parameter",message));
        if(string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>200)Add("$.name","名称必须为 1～200 个非空字符。");
        if(!ModuleName(p.WrapperModule))Add("$.wrapperModule","必须是 1～127 字符的已加载模块文件名，不能包含路径。");
        if(p.RuntimeVersion<MinimumRuntime || p.RuntimeVersion>MaximumRuntime)Add("$.runtimeVersion","仅支持 FMOD 2.02/2.03 接口系列。");
        if(!Modes.Contains(p.Locator))Add("$.locator","仅支持 wrapper 或 studioHandle。");
        if(p.Locator=="wrapper" && p.WrapperRva==0)Add("$.wrapperRva","wrapper 定位要求非零 RVA。");
        if(p.Locator=="studioHandle" && p.StudioHandle==0)Add("$.studioHandle","studioHandle 定位要求非零句柄。");
        if(p.Locator=="wrapper" && p.StudioHandle!=0)Add("$.studioHandle","wrapper 定位不能同时指定 Studio 句柄。");
        if(p.Locator=="studioHandle" && (p.WrapperRva!=0 || p.StateOffset!=0 || p.StudioOffset!=0))Add("$.locator","studioHandle 定位不能同时指定 wrapper RVA 或偏移。");
        if(p.StateOffset>4092)Add("$.stateOffset","状态偏移不能超过 4092。");
        if(p.StudioOffset>4088)Add("$.studioOffset","Studio 偏移不能超过 4088。");
        if(p.Buses==null || p.Buses.Count is <1 or >MaxBuses){Add("$.buses","必须提供 1～64 条总线。");return errors;}
        var ids=new Dictionary<string,FmodBus>(StringComparer.OrdinalIgnoreCase);var stable=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<p.Buses.Count;i++)
        {
            var b=p.Buses[i];string at=$"$.buses[{i}]";
            if(b==null){Add(at,"总线不可为 null。");continue;}
            if(!Regex.IsMatch(b.Id ?? "","^[0-9a-fA-F]{32}$") || !ids.TryAdd(b.Id!,b))Add(at+".id","必须是唯一的 32 位十六进制 FMOD GUID。");
            if(string.IsNullOrWhiteSpace(b.Name) || b.Name.Length>200)Add(at+".name","名称必须为 1～200 个非空字符。");
            if(b.Path==null || b.Path.Length>1024)Add(at+".path","描述路径不能为 null 或超过 1024 字符。");
            if(b.Category==null || b.Category.Length>80)Add(at+".category","分类不能为 null 或超过 80 字符。");
            string? id=b.StableId ?? b.Id;
            if(string.IsNullOrWhiteSpace(id) || id.Length>200 || !stable.Add(id))Add(at+".stableId","稳定 ID 必须唯一，长度为 1～200。");
        }
        for(int i=0;i<p.Buses.Count;i++)
        {
            var b=p.Buses[i];if(b==null)continue;
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){b.Id};string? parent=b.ParentId;
            while(parent!=null)
            {
                if(!ids.TryGetValue(parent,out var next) || !seen.Add(parent)){Add($"$.buses[{i}].parentId","父总线必须存在且不能成环。");break;}parent=next.ParentId;
            }
        }
        return errors;
    }
    public static bool ModuleName(string? name)=>!string.IsNullOrWhiteSpace(name) && name.Length<=MaxModuleCharacters &&
        name.IndexOfAny(['/', '\\', ':', '\0', '<', '>', '"', '|', '?', '*'])<0 && !name.Any(char.IsControl) && name is not ("." or "..") && !name.EndsWith('.') && !name.EndsWith(' ');
    public static void Require(JsonElement json,string file)
    {
        var error=Check(json,file).FirstOrDefault();
        if(error!=null)throw new OperationException("backend_unsupported",error.File+" "+error.Path+"："+error.Message,"edit_parameters",new Dictionary<string,string>{{"file",error.File},{"field",error.Path},{"reason",error.Code}});
    }
    public static bool MatchesSupport(ParameterSupport declared)=>System.Text.Json.Nodes.JsonNode.DeepEquals(
        JsonSerializer.SerializeToNode(Support.Parameters["studio-v1"],Wire.Json),JsonSerializer.SerializeToNode(declared,Wire.Json));
}
