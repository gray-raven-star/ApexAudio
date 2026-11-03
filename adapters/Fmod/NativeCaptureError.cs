using ApexAudio.Contracts;

namespace ApexAudio.Fmod;

static class NativeCaptureError
{
    internal static void RequireSuccess(string stage,uint result)
    {
        if(result==0)return;
        var details=new Dictionary<string,string>{{"stage",stage},{"nativeResult",result.ToString()}};
        if(result==100 || stage=="start" && result==108)
            throw new OperationException("capture_busy","此游戏的 FMOD 采集已被其他会话使用，请先关闭原连接。","close_other_session",details);
        if(stage=="start" && result==3)
            throw new OperationException("capture_not_ready","游戏音频实例尚不可用；请等待音频初始化后重连，持续失败时反馈适配作者。","wait_game_audio",details);
        throw new OperationException("capture_failed",$"FMOD 采集操作失败（{stage}：{result}），请将错误信息反馈适配作者。","check_capture_configuration",details);
    }
}
