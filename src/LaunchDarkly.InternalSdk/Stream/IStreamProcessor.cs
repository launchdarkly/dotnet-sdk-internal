using System;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Internal.Stream
{
    // Interface for platform-specific implementations of the streaming connection,
    // called from StreamManager.
    public interface IStreamProcessor
    {
        /// <summary>
        /// Handle a message from the stream. Implementations of this method should be async.
        /// </summary>
        /// <param name="streamManager">the StreamManager instance; this is passed so
        /// that you can set its Initialized property or call Restart if necessary</param>
        /// <param name="messageType">the SSE event type</param>
        /// <param name="messageData">the event data, as a string</param>
        /// <returns>nothing; implementations should be "async void"</returns>
        Task HandleMessage(StreamManager streamManager, string messageType, string messageData);

        /// <summary>
        /// Called when the stream has encountered an error.
        /// </summary>
        /// <remarks>
        /// StreamManager already implements standard retry/backoff behavior, so this method only
        /// needs to handle any additional error reporting that is desired.
        /// </remarks>
        /// <param name="streamManager">the StreamManager instance; this is passed so
        /// that you can set its Initialized property or call Restart if necessary</param>
        /// <param name="e">the exception; for HTTP error responses, this will be an
        /// <c>EventSource.EventSourceServiceUnsuccessfulResponseException</c></param>
        /// <param name="recoverable">true if StreamManager would normally retry after this type
        /// of error, false if it is a permanent failure</param>
        void HandleError(StreamManager streamManager, Exception e, bool recoverable);
    }
}
