using System;
using System.Diagnostics;

namespace PlcCommunication.Utilities
{
    /// <summary>
    /// High-resolution timer wrapper for measuring operation duration.
    /// Uses <see cref="Stopwatch"/> internally.
    /// </summary>
    public class SoftTimer
    {
        private readonly Stopwatch _stopwatch;

        public SoftTimer()
        {
            _stopwatch = new Stopwatch();
        }

        /// <summary>Start or restart the timer.</summary>
        public void Start()
        {
            _stopwatch.Restart();
        }

        /// <summary>Stop the timer.</summary>
        public void Stop()
        {
            _stopwatch.Stop();
        }

        /// <summary>Get elapsed time in milliseconds since last <see cref="Start"/>.</summary>
        public long ElapsedMs => _stopwatch.ElapsedMilliseconds;
    }
}
