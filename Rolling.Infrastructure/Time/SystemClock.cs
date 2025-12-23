using Rolling.Application.Abstractions.Time;

namespace Rolling.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
