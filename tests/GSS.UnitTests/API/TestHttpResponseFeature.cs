using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GSS.UnitTests.API;

/// <summary>
/// <see cref="IHttpResponseFeature"/> that honours <see cref="HttpResponse.HasStarted"/> and records <c>OnStarting</c> callbacks.
/// </summary>
public sealed class TestHttpResponseFeature : IHttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStartingCallbacks = new();

    /// <inheritdoc />
    public int StatusCode { get; set; } = 200;

    /// <inheritdoc />
    public string? ReasonPhrase { get; set; }

    /// <inheritdoc />
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    /// <inheritdoc />
    public Stream Body { get; set; } = Stream.Null;

    /// <inheritdoc />
    public bool HasStarted { get; set; }

    /// <inheritdoc />
    public void OnStarting(Func<object, Task> callback, object state)
    {
        _onStartingCallbacks.Add((callback, state));
    }

    /// <inheritdoc />
    public void OnCompleted(Func<object, Task> callback, object state)
    {
        // Not needed by these tests.
    }

    /// <summary>Runs the OnStarting callbacks, as the server does.</summary>
    public async Task FireOnStartingAsync()
    {
        foreach ((var callback, object state) in _onStartingCallbacks)
        {
            await callback(state);
        }
    }
}
