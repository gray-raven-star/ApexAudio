using System.Text.Json;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed record PackageVersionState(InstalledPack Pack, bool Current, bool InUse, IReadOnlyList<OperationError> Issues)
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
                result.Add(new(directory,manifest,DataRoot));
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
    public IReadOnlyList<OperationError> Readiness(InstalledPack pack)
    {
        var issues=new List<OperationError>();
        try { Validate(pack.Directory,pack.Manifest); }
        catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException) { return new[]{new OperationError("package_invalid",e.Message,"reimport_package")}; }
        foreach(var dependency in pack.Manifest.Profiles.Where(_=>pack.Manifest.SchemaVersion==2).Select(p=>p.Backend).Distinct())
            try { new BackendStore(DataRoot).Resolve(dependency); }
            catch(OperationException e) { issues.Add(e.Error); }
            catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException) { issues.Add(new("backend_unsupported",e.Message,"import_backend")); }
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
        var all=List();var issues=CatalogIssues.ToList();var result=new List<PackageVersionState>();
        foreach(var pack in all)
        {
            bool current=false;
            try {current=Same(GetCurrent(pack.Manifest.Id)?.Reference,pack.Reference);}
            catch(Exception e) when(e is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException)
            {issues.Add(new("package_invalid",e.Message,"choose_package_version",new Dictionary<string,string>{{"id",pack.Manifest.Id}}));}
            result.Add(new(pack,current,IsInUse(pack),Readiness(pack)));
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
