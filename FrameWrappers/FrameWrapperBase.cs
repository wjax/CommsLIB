using CommsLIB.Base;
using System;
using System.Threading.Tasks;

namespace CommsLIB.Communications.FrameWrappers
{
    public abstract class FrameWrapperBase<T>
    {
        // Delegate and event
        public delegate void FrameAvailableDelegate(string ID, T payload);
        public event FrameAvailableDelegate FrameAvailableEvent;

        private bool useThreadPool4Event;
        public string ID { get; private set; }

        private BlockingQueue<T> fireQueue;
        private Task fireTask;


        public FrameWrapperBase(bool _useThreadPool4Event)
        {
            if (_useThreadPool4Event)
            {
                fireQueue = new BlockingQueue<T>();
                fireTask = new Task(FireQueuedEventLoop, TaskCreationOptions.LongRunning);
                fireTask.Start();
            }
                
        }

        public void SetID(string _id)
        {
            ID = _id;
        }

        public abstract void AddBytes(byte[] bytes, int length);

        public abstract void Start();

        public abstract void Stop();

        public void FireEvent(T toFire)
        {
            if (useThreadPool4Event)
                fireQueue.Enqueue(toFire);
            else
                FrameAvailableEvent?.Invoke(ID, toFire);
        }

        private void FireQueuedEventLoop()
        {
            // TODO Stop
            while(true)
            {
                T toFire = fireQueue.Dequeue();
                FrameAvailableEvent?.Invoke(ID, toFire);
            }
        }

        public void UnsubscribeEventHandlers()
        {
            if (FrameAvailableEvent != null)
                foreach (var d in FrameAvailableEvent.GetInvocationList())
                    FrameAvailableEvent -= (d as FrameAvailableDelegate);
        }

        public virtual byte[] Data2BytesSync(T data, out int count)
        {
            throw new NotImplementedException();
        }
    }
}
