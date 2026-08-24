using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace BambuLab.X1Camera;

/// <summary>
/// Reads one camera session and broadcasts complete access units to independent bounded consumers.
/// The caller retains ownership of the session.
/// </summary>
public sealed class X1CameraAccessUnitHub : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<long, Subscriber> subscribers = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task pump;
    private long nextSubscriberId;
    private int disposed;

    /// <summary>Starts broadcasting access units read from <paramref name="session"/>.</summary>
    public X1CameraAccessUnitHub(X1CameraSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        pump = PumpAsync(session);
    }

    /// <summary>Creates a consumer with an independent bounded queue.</summary>
    /// <param name="capacity">Maximum queued access units before the oldest queued unit is dropped.</param>
    public X1CameraAccessUnitSubscription Subscribe(int capacity = 8)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (capacity is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        var channel = Channel.CreateBounded<H264AccessUnit>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            var id = ++nextSubscriberId;
            var subscriber = new Subscriber(channel);
            subscribers.Add(id, subscriber);
            return new X1CameraAccessUnitSubscription(
                channel.Reader,
                () => subscriber.DroppedAccessUnits,
                () => Unsubscribe(id));
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }

        CompleteSubscribers(null);
        lifetime.Dispose();
    }

    private async Task PumpAsync(X1CameraSession session)
    {
        Exception? failure = null;
        try
        {
            await foreach (var accessUnit in session.ReadAccessUnitsAsync(lifetime.Token).ConfigureAwait(false))
            {
                Subscriber[] snapshot;
                lock (gate)
                {
                    snapshot = [.. subscribers.Values];
                }

                foreach (var subscriber in snapshot)
                {
                    subscriber.Publish(accessUnit);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            CompleteSubscribers(failure);
        }
    }

    private void Unsubscribe(long id)
    {
        Subscriber? subscriber;
        lock (gate)
        {
            subscribers.Remove(id, out subscriber);
        }

        subscriber?.Complete(null);
    }

    private void CompleteSubscribers(Exception? failure)
    {
        Subscriber[] snapshot;
        lock (gate)
        {
            snapshot = [.. subscribers.Values];
            subscribers.Clear();
        }

        foreach (var subscriber in snapshot)
        {
            subscriber.Complete(failure);
        }
    }

    private sealed class Subscriber(Channel<H264AccessUnit> channel)
    {
        private long droppedAccessUnits;

        public long DroppedAccessUnits => Interlocked.Read(ref droppedAccessUnits);

        public void Publish(H264AccessUnit accessUnit)
        {
            if (channel.Writer.TryWrite(accessUnit))
            {
                return;
            }

            if (channel.Reader.TryRead(out _))
            {
                Interlocked.Increment(ref droppedAccessUnits);
            }

            if (!channel.Writer.TryWrite(accessUnit))
            {
                Interlocked.Increment(ref droppedAccessUnits);
            }
        }

        public void Complete(Exception? failure) => channel.Writer.TryComplete(failure);
    }
}

/// <summary>An independent bounded access-unit consumer created by <see cref="X1CameraAccessUnitHub"/>.</summary>
public sealed class X1CameraAccessUnitSubscription : IAsyncDisposable
{
    private readonly ChannelReader<H264AccessUnit> reader;
    private readonly Func<long> getDroppedAccessUnits;
    private Action? unsubscribe;

    internal X1CameraAccessUnitSubscription(
        ChannelReader<H264AccessUnit> reader,
        Func<long> getDroppedAccessUnits,
        Action unsubscribe)
    {
        this.reader = reader;
        this.getDroppedAccessUnits = getDroppedAccessUnits;
        this.unsubscribe = unsubscribe;
    }

    /// <summary>Gets the number of access units dropped because this consumer's queue was full.</summary>
    public long DroppedAccessUnits => getDroppedAccessUnits();

    /// <summary>Reads access units until cancellation, disposal, or source completion.</summary>
    public async IAsyncEnumerable<H264AccessUnit> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var accessUnit in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return accessUnit;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        return ValueTask.CompletedTask;
    }
}
