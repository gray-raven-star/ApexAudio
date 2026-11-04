using System.Text.Json;
using System.Text.RegularExpressions;
using ApexAudio.Contracts;

namespace ApexAudio.Fmod.Definition;

// First-party, data-only validation; contains no capture or process operations.
public sealed class FmodConfigurationValidator : IBackendConfigurationValidator
{
    public IReadOnlyList<ConfigurationDiagnostic> Validate(BackendDescriptor descriptor,CaptureProfile profile,JsonElement parameters,IReadOnlyList<GameMatch> builds,string file)
    {
        if(descriptor.Id!="apex.fmod" || descriptor.Version!="0.3.0" || descriptor.ContextVersion!=2 || profile.Backend.Variant!="studio-v1" ||
            descriptor.Support?.Parameters.TryGetValue(profile.Backend.Variant,out var support)!=true || support==null || !FmodParameterContract.MatchesSupport(support))
            return new[]{new ConfigurationDiagnostic(file,"$","backend_unsupported","本程序未实现该后端声明的参数契约，请使用匹配版本的主程序或后端。")};
        var errors=FmodParameterContract.Check(parameters,file).ToList();if(errors.Count!=0)return errors;
        var value=parameters.Deserialize<FmodParameters>(Wire.Json)!;
        for(int i=0;i<builds.Count;i++)
        {
            var build=builds[i];if(build.ProfileId!=profile.Id)continue;
            if(string.IsNullOrWhiteSpace(build.Executable) || Path.GetFileName(build.Executable)!=build.Executable)
                errors.Add(new("manifest.json",$"$.games[{i}].executable","invalid_module","游戏程序必须是文件名，不能包含路径。"));
            var modules=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var(name,hash)in build.Modules ?? new())
                if(!FmodParameterContract.ModuleName(name) || !modules.TryAdd(name,hash))
                    errors.Add(new("manifest.json",$"$.games[{i}].modules.{name}","invalid_module","模块名称无效或重复。"));
            foreach(string name in new[]{"fmod.dll","fmodstudio.dll",value.WrapperModule})
                if(!modules.TryGetValue(name,out string? hash) || !Regex.IsMatch(hash ?? "","^[a-fA-F0-9]{64}$"))
                    errors.Add(new("manifest.json",$"$.games[{i}].modules.{name}","required_evidence","必须声明关键模块的实际 SHA-256。"));
        }
        return errors;
    }
}
