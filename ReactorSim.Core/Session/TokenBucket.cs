// ReactorSim.Core - rate/burst token bucket on injected seconds (Phase 3, U4, KTD10 step 4).

using System;

namespace ReactorSim.Session
{
    public sealed class TokenBucket
    {
        // Floating point slack so that exactly rate * dt = 1 token counts as one (0.1 s at 10 per second).
        private const double Epsilon = 1e-9;

        private readonly double _rate;
        private readonly double _burst;
        private double _tokens;
        private double _last;

        public TokenBucket(double ratePerSecond, double burst, double nowSeconds)
        {
            if (!(ratePerSecond > 0) || double.IsInfinity(ratePerSecond))
                throw new ArgumentOutOfRangeException(nameof(ratePerSecond));
            if (!(burst >= 1) || double.IsInfinity(burst))
                throw new ArgumentOutOfRangeException(nameof(burst));
            _rate = ratePerSecond;
            _burst = burst;
            _tokens = burst;
            _last = nowSeconds;
        }

        // A time earlier than the last one counts as no time passing and does not rewind the clock.
        public bool TryTake(double nowSeconds)
        {
            if (nowSeconds > _last)
            {
                _tokens = Math.Min(_burst, _tokens + (nowSeconds - _last) * _rate);
                _last = nowSeconds;
            }
            if (_tokens >= 1.0 - Epsilon)
            {
                _tokens = Math.Max(0.0, _tokens - 1.0);
                return true;
            }
            return false;
        }
    }
}
