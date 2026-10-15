namespace ApexAudio.Wwise;

// An idle render clock is normal during loading, focus loss and some menus.
// It affects status, not the lifetime of the capture connection.
internal sealed class RenderProgress(long started, long frequency)
{
    long lastProgress = started;
    int lastFrame;
    public bool Waiting { get; private set; }
    public long Observe(int frame, long now)
    {
        long gap = 0;
        if (frame != lastFrame)
        {
            if (Waiting) gap = Math.Max(0, now - lastProgress - (long)(frame - lastFrame) * frequency / 48000);
            lastFrame = frame; lastProgress = now;
        }
        Waiting = now - lastProgress > frequency * 10;
        return gap;
    }
}
