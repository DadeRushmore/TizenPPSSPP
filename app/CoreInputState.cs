// UI-thread-owned cache of writes, not queued events. Every changed mask is sent,
// including a press followed by a release before the next native input_poll.
// Forget after every native reset so a held value is resent on resume.
internal sealed class CoreInputState
{
    bool buttonsKnown,sticksKnown;
    uint buttons;
    int lx,ly,rx,ry;
    internal long ButtonRequests,ButtonWrites,StickRequests,StickWrites;
    internal bool ButtonsChanged(uint value) {
        ButtonRequests++;
        if(buttonsKnown&&buttons==value)return false;
        buttonsKnown=true;buttons=value;ButtonWrites++;return true;
    }
    internal bool SticksChanged(int x,int y,int z,int w) {
        StickRequests++;
        if(sticksKnown&&lx==x&&ly==y&&rx==z&&ry==w)return false;
        sticksKnown=true;lx=x;ly=y;rx=z;ry=w;StickWrites++;return true;
    }
    internal void Forget(){buttonsKnown=sticksKnown=false;}
    internal string Summary=>"INPUT_FORWARD button_requests="+ButtonRequests+" button_writes="+ButtonWrites+
        " stick_requests="+StickRequests+" stick_writes="+StickWrites;
}
