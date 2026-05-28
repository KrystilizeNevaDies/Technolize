using System.Collections.Concurrent;
using Technolize.World;

namespace Technolize.Runtime;

public sealed class WorldCommandQueue
{
    private readonly ConcurrentQueue<Action<TickableWorld>> _commands = new();

    public void Enqueue(Action<TickableWorld> command)
    {
        _commands.Enqueue(command);
    }

    public bool Drain(TickableWorld world)
    {
        bool executedAny = false;

        while (_commands.TryDequeue(out Action<TickableWorld>? command))
        {
            command(world);
            executedAny = true;
        }

        return executedAny;
    }
}