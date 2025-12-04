using System;

namespace GpuWorker;

public class BackoffStrategy
{
    private readonly TimeSpan _initial = TimeSpan.FromMilliseconds(250);
    private readonly TimeSpan _max = TimeSpan.FromSeconds(10);
    private TimeSpan _nextDelay;
    private bool _initialized;

    public BackoffStrategy()
    {
        _nextDelay = _initial;
    }

    public virtual TimeSpan NextDelay()
    {
        TimeSpan delay;
        if (!_initialized)
        {
            _initialized = true;
            delay = _initial;
        }
        else
        {
            delay = _nextDelay;
        }

        var multiplied = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _max.TotalMilliseconds));
        _nextDelay = multiplied;
        return delay;
    }

    public virtual void Reset()
    {
        _initialized = false;
        _nextDelay = _initial;
    }
}

