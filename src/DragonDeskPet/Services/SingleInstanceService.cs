using System.IO;
using System.IO.Pipes;
using System.Text;

namespace DragonDeskPet.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;

    public SingleInstanceService(string instanceName = "DragonDeskPet.Singleton.v1")
    {
        var safeName = string.Concat(instanceName.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_'));
        _pipeName = safeName + ".pipe";
        _mutex = new Mutex(initiallyOwned: true, $"Local\\{safeName}", out var createdNew);
        _ownsMutex = createdNew;
        IsPrimaryInstance = createdNew;

        if (IsPrimaryInstance)
        {
            _listenerTask = ListenAsync(_cancellation.Token);
        }
    }

    public bool IsPrimaryInstance { get; }

    public event EventHandler? ShowRequested;

    public async Task<bool> SignalPrimaryAsync(TimeSpan? timeout = null)
    {
        if (IsPrimaryInstance)
        {
            return false;
        }

        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        using var timeoutCancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        try
        {
            await client.ConnectAsync(timeoutCancellation.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            await writer.WriteLineAsync("show").ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, "show", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already releasing ownership during shutdown.
            }
        }

        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
