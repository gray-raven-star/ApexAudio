using System.Text.Json;
using ApexAudio.Contracts;

namespace ApexAudio.Core;

public sealed class PreparedSession : IDisposable
{
    IDisposable? use;
    int state;
    public Guid Id { get; }=Guid.NewGuid();
    public InstalledPack Pack { get; }
    public PackageVersionRef Package { get; }
    public string ProgramRoot { get; }
    public string Executable { get; }
    readonly JsonElement? contextSnapshot,matchSnapshot;
    public LaunchContext? Context => contextSnapshot?.Deserialize<LaunchContext>(Wire.Json);
    public BuildMatchResult? Match => matchSnapshot?.Deserialize<BuildMatchResult>(Wire.Json);
    PreparedSession(InstalledPack pack,string root,string executable,LaunchContext? context,BuildMatchResult? match,IDisposable? use)
    {
        Pack=pack;Package=pack.Reference;ProgramRoot=root;Executable=executable;this.use=use;
        if(context!=null)contextSnapshot=Wire.Element(context);
        if(match!=null)matchSnapshot=Wire.Element(match);
    }
    public static PreparedSession Prepare(InstalledPack pack,string? gamePath=null,int? processId=null)
    {
        IDisposable? use=null;
        try
        {
            if(pack.DataRoot!=null)
            {
                var store=new PackageStore(pack.DataRoot);pack=store.GetVersion(pack.Reference);use=store.AcquireUse(pack);
            }
            PackageStore.Validate(pack.Directory,pack.Manifest);
            if(pack.Manifest.SchemaVersion==1)return new(pack,pack.Directory,pack.Manifest.EntryPoint,null,null,use);
            BuildMatchResult? match=null;TargetIdentity? target=null;CaptureProfile profile;
            if(gamePath==null && processId is not >0)profile=V2Packages.SelectOffline(pack);
            else
            {
                if(processId==null)throw new OperationException("target_changed","连接准备缺少目标进程。","choose_target");
                target=TargetIdentity.Read(processId.Value);target.Validate(new(processId.Value,gamePath));
                var evidence=TargetEvidence.Collect(target,pack.Manifest.Games.SelectMany(g=>g.Modules?.Keys.AsEnumerable() ?? Array.Empty<string>()));
                match=BuildMatcher.Match(pack.Manifest.Games,evidence);
                if(match.Failure is {} error)throw new OperationException(error.Code,error.Message,error.NextAction,error.Details);
                profile=pack.Manifest.Profiles.Single(p=>p.Id==match.ProfileId);
            }
            string dataRoot=pack.DataRoot ?? throw new IOException("共享后端准备需要明确的数据仓库位置。");
            string root=new BackendStore(dataRoot).Resolve(profile.Backend);var descriptor=BackendFiles.Read(root,profile.Backend);
            string parameterFile=PackageStore.SafePath(pack.Directory,profile.Parameters);
            int maxBytes=descriptor.Support?.Parameters[profile.Backend.Variant].MaxBytes ?? 4194304;
            if(new FileInfo(parameterFile).Length>maxBytes)throw new OperationException("backend_unsupported","参数文件超过后端支持的大小。","edit_parameters",new Dictionary<string,string>{{"file",profile.Parameters}});
            using var parameters=JsonDocument.Parse(File.ReadAllText(parameterFile));
            BackendFiles.ValidateParameterHeader(descriptor,profile.Backend,parameters.RootElement,profile.Parameters);
            var candidates=descriptor.ContextVersion==2?match?.Candidates:null;
            var context=new LaunchContext(descriptor.ContextVersion,pack.Manifest.Id,pack.Manifest.Version,pack.Reference.ManifestSha256,
                profile.Id,profile.Backend,parameters.RootElement.Clone(),candidates?.FirstOrDefault(),descriptor.ContextVersion==2?target:null,candidates,profile.Parameters);
            return new(pack,root,descriptor.EntryPoint,context,match,use);
        }
        catch{use?.Dispose();throw;}
    }
    internal IDisposable? TakeUse()
    {
        if(Interlocked.CompareExchange(ref state,1,0)!=0)throw new InvalidOperationException("准备会话已被使用或释放。");
        return Interlocked.Exchange(ref use,null);
    }
    public void Dispose()
    {
        if(Interlocked.CompareExchange(ref state,2,0)==0)Interlocked.Exchange(ref use,null)?.Dispose();
    }
}
