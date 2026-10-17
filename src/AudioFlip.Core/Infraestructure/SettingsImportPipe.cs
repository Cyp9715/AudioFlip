namespace AudioFlip;

using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

public sealed class SettingsImportPipe : IDisposable
{
    private readonly string name;
    private readonly Func<AudioSwitcherState, Task<IReadOnlyList<string>>> apply;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task serverTask;
    private int disposed;

    public SettingsImportPipe(string name, Func<AudioSwitcherState, Task<IReadOnlyList<string>>> apply)
    {
        this.name = name;
        this.apply = apply;
        serverTask = Task.Run(ListenAsync);
    }

    public static async Task<IReadOnlyList<string>> SendAsync(string name, AudioSwitcherState state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var pipe = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(5000, timeout.Token);
        }
        catch (TimeoutException e)
        {
            throw new IOException("Cannot reach the running AudioFlip. Restart it and retry.", e);
        }

        var json = new SettingsBackup { Settings = state }.ToJson();
        await WriteAsync(pipe, Encoding.UTF8.GetBytes(json), timeout.Token);
        var reply = JsonSerializer.Deserialize<Reply>(await ReadAsync(pipe, timeout.Token))
            ?? throw new IOException("AudioFlip returned an invalid import result.");
        if (reply.Error != null)
            throw new AudioSwitcherException(reply.Error);
        return reply.Warnings;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        shutdown.Cancel();
        _ = serverTask.ContinueWith(_ => shutdown.Dispose(), TaskScheduler.Default);
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(
                    name,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(shutdown.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    Reply reply;
                    try
                    {
                        var request = await ReadAsync(pipe, timeout.Token);
                        var state = SettingsBackup.Parse(Encoding.UTF8.GetString(request)).Settings;
                        var warnings = await apply(state);
                        reply = new(null, warnings.ToArray());
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        reply = new(e.Message, Array.Empty<string>());
                    }

                    await WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(reply), timeout.Token);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException)
                {
                    // One disconnected or timed-out client must not stop the listener.
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > SettingsBackup.MaximumFileSize)
            throw new InvalidDataException("Invalid settings message size.");
        var message = new byte[length];
        await stream.ReadExactlyAsync(message, token);
        return message;
    }

    private static async Task WriteAsync(Stream stream, byte[] message, CancellationToken token)
    {
        if (message.Length > SettingsBackup.MaximumFileSize)
            throw new InvalidDataException("The settings message exceeds the 16 MB size limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(message, token);
        await stream.FlushAsync(token);
    }

    private sealed record Reply(string? Error, string[] Warnings);
}
