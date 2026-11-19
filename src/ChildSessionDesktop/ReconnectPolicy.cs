using System;

namespace ChildSessionDesktop
{
    // Connection retry state kept separate from the RDP control and UI timers.
    internal sealed class ReconnectPolicy
    {
        public const int MaxAttempts = 6;

        private int _attempts;

        public bool InProgress { get; private set; }
        public int Attempts { get { return _attempts; } }
        public bool GaveUp { get; private set; }

        public static bool ShouldAutoReconnect(int disconnectReason)
        {
            return disconnectReason != 1;
        }

        public void Enter()
        {
            if (InProgress) return;
            InProgress = true;
            _attempts = 0;
            GaveUp = false;
        }

        public void OnAttempt()
        {
            if (!InProgress) return;
            _attempts++;
            if (_attempts > MaxAttempts)
            {
                GaveUp = true;
                InProgress = false;
            }
        }

        public void OnReconnected()
        {
            InProgress = false;
            _attempts = 0;
            GaveUp = false;
        }

        public void OnManualReconnect()
        {
            InProgress = false;
            _attempts = 0;
            GaveUp = false;
        }
    }
}
