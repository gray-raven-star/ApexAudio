using System.Text.Json;
using ApexAudio.Authoring;
using ApexAudio.Contracts;
using ApexAudio.Core;

bool json=args.Contains("--json");
try
{
    var positional=new List<string>();var options=new Dictionary<string,string>();var backends=new List<string>();
    for(int i=0;i<args.Length;i++)
    {
        if(args[i]=="--json")continue;
        if(args[i].StartsWith("--"))
        {
            string key=args[i];if(i+1==args.Length || args[i+1].StartsWith("--"))throw new ArgumentException("选项缺少值："+key);
            string value=args[++i];if(key=="--backend")backends.Add(value);
            else if(key is "--id" or "--name" or "--version" or "--variant")options.Add(key,value);
            else throw new ArgumentException("未知选项："+key);
        }
        else positional.Add(args[i]);
    }
    string? command=positional.FirstOrDefault();
    if(positional.Count==2 && command is not ("create" or "validate" or "package" or "describe")){positional.Insert(0,"package");command="package";}
    string Get(string key,string fallback)=>options.GetValueOrDefault(key,fallback);
    if(command=="describe" && positional.Count==2)Console.WriteLine(JsonSerializer.Serialize(GamepackAuthoring.Describe(positional[1]),Wire.Json));
    else if(command=="create" && positional.Count==2 && backends.Count==1)
    {
        GamepackAuthoring.Create(positional[1],backends[0],Get("--id","community.example"),Get("--name","TODO 游戏名称"),Get("--version","0.1.0"),Get("--variant","studio-v1"));
        Console.WriteLine("已创建未完成的 JSON 模板；填写实际构建证据与参数后运行 validate。");
    }
    else if(command=="validate" && positional.Count==2)
    {
        var errors=GamepackAuthoring.Validate(positional[1],backends);
        Print(errors);return errors.Count==0?0:1;
    }
    else if(command=="package" && positional.Count==3)
    {
        var package=GamepackAuthoring.Package(positional[1],positional[2],backends);
        Console.WriteLine(JsonSerializer.Serialize(new{package,archive=Path.GetFullPath(positional[2]),sha256=PackageStore.Hash(positional[2])},Wire.Json));
    }
    else
    {
        Console.WriteLine("ApexAudio.PackV2 create <new-directory> --backend <backend-directory> [--id <id>] [--name <name>] [--version <version>] [--variant studio-v1]\nApexAudio.PackV2 validate <definition-directory> --backend <backend-directory> [--json]\nApexAudio.PackV2 package <definition-directory> <output.gpack> --backend <backend-directory>\nApexAudio.PackV2 describe <backend-directory>\n--backend 可重复，以提供各 Profile 的确切依赖。工具只读文件，不启动后端或连接游戏。");return 1;
    }
    return 0;
}
catch(AuthoringValidationException e){Print(e.Diagnostics);return 1;}
catch(Exception e)when(e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
{Print(new[]{new ConfigurationDiagnostic("",(e as JsonException)?.Path ?? "$",(e as OperationException)?.Error.Code ?? "operation_failed",e.Message)});return 1;}
void Print(IReadOnlyList<ConfigurationDiagnostic> errors)
{
    if(json)Console.WriteLine(JsonSerializer.Serialize(new{valid=errors.Count==0,diagnostics=errors},Wire.Json));
    else if(errors.Count==0)Console.WriteLine("静态条件满足；未执行后端或验证真实游戏兼容性。");
    else foreach(var error in errors)Console.WriteLine($"{error.File} {error.Path} [{error.Code}] {error.Message}");
}