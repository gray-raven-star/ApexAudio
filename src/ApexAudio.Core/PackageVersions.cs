using System.Text.Json;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record ResolvedBackend(BackendDependency Dependency,BackendDescription Description);
public sealed record PackageVersionState(InstalledPack Pack, bool Current, bool InUse, IReadOnlyList<OperationError> Issues,IReadOnlyList<ResolvedBackend>? Backends=null)
{
    public bool Ready => Issues.Count == 0;
}

public sealed partial class PackageStore
{
    public string DataRoot { get; }
    public string VersionsRoot { get; }
    public IReadOnlyList<OperationError> CatalogIssues { get; private set; } = Array.Empty<OperationError>();
    string CurrentFile => Path.Combine(DataRoot,"package-current.json");
    FileStream LockCatalog() => new(Path.Combine(DataRoot,"package-catalog.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    Dictionary<string,PackageVersionRef?> ReadCurrent() => File.Exists(CurrentFile)
        ? JsonSerializer.Deserialize<Dictionary<string,PackageVersionRef?>>(File.ReadAllText(CurrentFile),Wire.Json)
            ?? throw new InvalidDataException("适配包当前选择记录为空。") : new();
    static bool Same(PackageVersionRef? a, PackageVersionRef b) => a != null && a.Id == b.Id && a.Version == b.Version &&
        string.Equals(a.ManifestSha256,b.ManifestSha256,StringComparison.OrdinalIgnoreCase);
    static void VerifyDirectory(string directory)
    {
        BackendFiles.PathIn(directory,"manifest.json");
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("适配包目录不能是链接。");
    }
    public List<InstalledPack> List()
    {
        var result = new List<InstalledPack>();
        var issues = new List<OperationError>();
        foreach (string directory in Directory.EnumerateDirectories(Root))
            if (!Path.GetFileName(directory).StartsWith('.') && File.Exists(Path.Combine(directory,"manifest.json"))) Add(directory,Root,Path.GetFileName(directory),null);
        foreach (string id in Directory.EnumerateDirectories(VersionsRoot).Where(p => !Path.GetFileName(p).StartsWith('.')))
        {
            try { BackendFiles.PathIn(VersionsRoot,Path.GetFileName(id)+"/manifest.json"); }
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException) { issues.Add(new("package_invalid",e.Message,"open_data_folder",new Dictionary<string,string>{{"id",Path.GetFileName(id)}}));continue; }
            foreach (string directory in Directory.EnumerateDirectories(id))
                Add(directory,VersionsRoot,Path.GetFileName(id),Path.GetFileName(directory));
        }
        CatalogIssues=issues;
        return result;
        void Add(string directory,string repository,string id,string? version)
        {
            try
            {
                BackendFiles.PathIn(repository,Path.GetRelativePath(repository,Path.Combine(directory,"manifest.json")).Replace('\\','/'));
                VerifyDirectory(directory);var manifest=ReadManifest(Path.Combine(directory,"manifest.json"));
                if(manifest.Id!=id || version!=null && manifest.Version!=version || !Version.TryParse(manifest.Version,out _))
                    throw new InvalidDataException("适配包目录与版本身份不一致。");
                var pack=new InstalledPack(directory,manifest,DataRoot);_ = pack.Reference;result.Add(pack);
            }
            catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException)
            {issues.Add(new("package_invalid","安装记录损坏，请打开数据目录处理该版本后重新导入："+e.Message,"open_data_folder",new Dictionary<string,string>{{"id",id},{"version",version??"legacy"},{"directory",directory}}));}
        }
    }
    public InstalledPack GetVersion(PackageVersionRef reference)
    {
        var matches=List().Where(p=>p.Manifest.Id==reference.Id && p.Manifest.Version==reference.Version).ToArray();
        if(matches.Length!=1 || !Same(matches[0].Reference,reference)) throw new OperationException("package_invalid","适配包版本不存在或内容已改变。","choose_package_version");
        return matches[0];
    }
    public InstalledPack? GetCurrent(string id)
    {
        var selections=ReadCurrent();
        if(selections.TryGetValue(id,out var selected)) return selected==null?null:GetVersion(selected);
        // Existing single-version installations retain their previous selection.
        string legacy=SafePath(Root,id);
        if(!File.Exists(Path.Combine(legacy,"manifest.json"))) return null;
        BackendFiles.PathIn(Root,id+"/manifest.json");
        var manifest=ReadManifest(Path.Combine(legacy,"manifest.json"));
        if(manifest.Id!=id) throw new InvalidDataException("旧适配包目录与身份不一致。");
        return new(legacy,manifest,DataRoot);
    }
    public List<InstalledPack> Current()
    {
        var result=new List<InstalledPack>();var all=List();var issues=CatalogIssues.ToList();
        foreach(string id in all.Select(p=>p.Manifest.Id).Distinct())
            try {if(GetCurrent(id) is {} current)result.Add(current);}
            catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException)
            {issues.Add(new("package_invalid",e.Message,"choose_package_version",new Dictionary<string,string>{{"id",id}}));}
        CatalogIssues=issues;return result;
    }
    public IReadOnlyList<OperationError> Readiness(InstalledPack pack)=>Readiness(pack,new Dictionary<BackendDependency,BackendDescriptor>());
    IReadOnlyList<OperationError> Readiness(InstalledPack pack,Dictionary<BackendDependency,BackendDescriptor> backends)
    {
        var issues=new List<OperationError>();
        try { Validate(pack.Directory,pack.Manifest); }
        catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException) { return new[]{new OperationError("package_invalid",e.Message,"reimport_package")}; }
        foreach(var profile in pack.Manifest.Profiles.Where(_=>pack.Manifest.SchemaVersion==2))
            try
            {
                if(!backends.TryGetValue(profile.Backend,out var descriptor))
                {
                    string root=new BackendStore(DataRoot).Resolve(profile.Backend);descriptor=BackendFiles.Read(root,profile.Backend);
                    backends.Add(profile.Backend,descriptor);
                }
                if(descriptor.Support==null)continue; // Preserve historical workflows without claiming new validation.
                string file=SafePath(pack.Directory,profile.Parameters);
                if(new FileInfo(file).Length>descriptor.Support.Parameters[profile.Backend.Variant].MaxBytes)
                    throw new OperationException("backend_unsupported","参数文件超过后端支持的大小。","edit_parameters",new Dictionary<string,string>{{"file",profile.Parameters}});
                using var parameters=JsonDocument.Parse(File.ReadAllText(file));
                BackendFiles.ValidateParameterHeader(descriptor,profile.Backend,parameters.RootElement,profile.Parameters);
                if(configurationValidator==null)throw new OperationException("backend_unsupported","本程序缺少该参数契约的静态验证器，不能设为当前版本。","update_host");
                foreach(var error in configurationValidator.Validate(descriptor,profile,parameters.RootElement,pack.Manifest.Games,profile.Parameters))
                    issues.Add(new("backend_unsupported",error.File+" "+error.Path+"："+error.Message,"edit_parameters",new Dictionary<string,string>{{"file",error.File},{"field",error.Path},{"reason",error.Code}}));
            }
            catch(OperationException e) { issues.Add(e.Error); }
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { issues.Add(new("backend_unsupported",e.Message,"check_configuration")); }
        return issues;
    }
    public void SelectCurrent(string id, PackageVersionRef? reference)
    {
        using var catalog=LockCatalog();
        if(reference!=null)
        {
            if(reference.Id!=id) throw new OperationException("package_invalid","包 ID 与版本选择不一致。","choose_package_version");
            var issues=Readiness(GetVersion(reference));
            if(issues.Count!=0) { var e=issues[0]; throw new OperationException(e.Code,e.Message,e.NextAction,e.Details); }
        }
        else SafePath(Root,id);
        var selections=ReadCurrent();selections[id]=reference;PresetStore.AtomicWrite(CurrentFile,selections);
    }
    string LeasePath(InstalledPack pack)
    {
        string directory=SafePath(Path.Combine(DataRoot,"package-leases"),pack.Manifest.Id);
        Directory.CreateDirectory(directory);
        return SafePath(directory,pack.Manifest.Version+".lock");
    }
    public IDisposable AcquireUse(InstalledPack pack)
    {
        using var catalog=LockCatalog();
        GetVersion(pack.Reference); Validate(pack.Directory,pack.Manifest);
        return new FileStream(LeasePath(pack),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite);
    }
    public bool IsInUse(InstalledPack pack)
    {
        try { using var unused=new FileStream(LeasePath(pack),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);return false; }
        catch(IOException) {return true;}
    }
    public List<PackageVersionState> ListVersions()
    {
        var all=List();var issues=CatalogIssues.ToList();var result=new List<PackageVersionState>();var backends=new Dictionary<BackendDependency,BackendDescriptor>();
        foreach(var pack in all)
        {
            bool current=false;
            try {current=Same(GetCurrent(pack.Manifest.Id)?.Reference,pack.Reference);}
            catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException)
            {issues.Add(new("package_invalid",e.Message,"choose_package_version",new Dictionary<string,string>{{"id",pack.Manifest.Id}}));}
            var readiness=Readiness(pack,backends);
            var descriptions=pack.Manifest.Profiles.Where(p=>backends.ContainsKey(p.Backend)).Select(p=>
                {var d=backends[p.Backend];return new ResolvedBackend(p.Backend,new(d.Id,d.Version,p.Backend.Variant,d.ContextVersion,d.Support));}).Distinct().ToArray();
            result.Add(new(pack,current,IsInUse(pack),readiness,descriptions));
        }
        CatalogIssues=issues;return result;
    }
    public void RemoveVersion(PackageVersionRef reference)
    {
        using var catalog=LockCatalog();var pack=GetVersion(reference);
        if(Same(GetCurrent(reference.Id)?.Reference,reference)) throw new OperationException("current_version_selected","请先改选或清除当前版本。","choose_package_version");
        FileStream lease;
        try {lease=new(LeasePath(pack),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException) {throw new OperationException("version_in_use","此适配包版本正在使用，请先断开连接。","disconnect");}
        using(lease)
        {
            BackendFiles.PathIn(DataRoot,Path.GetRelativePath(DataRoot,Path.Combine(pack.Directory,"manifest.json")).Replace('\\','/'));
            VerifyDirectory(pack.Directory);Directory.Delete(pack.Directory,true);
        }
    }
}
