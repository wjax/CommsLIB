namespace CommsLIB.Communications.FrameWrappers
{
    public abstract class SyncFrameWrapper<T> : FrameWrapperBase<T>
    {
        public SyncFrameWrapper(bool _useThreadPool4Event) : base(_useThreadPool4Event)
        {

        }

        public override void Start()
        {
        }

        public override void Stop()
        {
        }

    }
}
