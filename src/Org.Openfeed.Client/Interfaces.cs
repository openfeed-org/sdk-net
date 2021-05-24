using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Org.Openfeed.Client {

    /// <summary>
    /// Exception that will be thrown when a request-response type of request is sent to the server and the response result
    /// is not <see cref="Result.Success"/>.
    /// </summary>
    public class OpenfeedRequestException : Exception {
        /// <summary>
        /// The <see cref="Result"/> of the failed operation.
        /// </summary>
        public readonly Result Result;

        /// <summary>
        /// Creates a new instance of the <see cref="OpenfeedRequestException"/> based on the <see cref="Status"/> of a response
        /// message.
        /// </summary>
        /// <param name="status"><see cref="Status"/> of the response message</param>
        public OpenfeedRequestException(Status status) : base(status.Message) {
            Result = status.Result;
        }

        /// <summary>
        /// Throws an <see cref="OpenfeedRequestException"/> in case the <see cref="Status.Result"/> is different
        /// from <see cref="Result.Success"/>.
        /// </summary>
        /// <param name="status"><see cref="Status"/> of the response message.</param>
        public static void ThrowOnError(Status status) {
            if (status.Result != Result.Success) throw new OpenfeedRequestException(status);
        }
    }

    /// <summary>
    /// Exception that will be thrown when attempting to send a request through a <see cref="IOpenfeedConnection"/> that has been
    /// disconnected.
    /// </summary>
    public class OpenfeedDisconnectedException : Exception {
        /// <summary>
        /// Creates a new instance of <see cref="OpenfeedDisconnectedException"/>.
        /// </summary>
        public OpenfeedDisconnectedException() : base("Disconnected from the server.") { }
    }

    /// <summary>
    /// Small structure that contains the exchange code and exchange description.
    /// </summary>
    public readonly struct Exchange {
        /// <summary>
        /// Exchange code.
        /// </summary>
        public readonly string Code;

        /// <summary>
        /// Exchange description.
        /// </summary>
        public readonly string Description;

        /// <summary>
        /// Constructs a new <see cref="Exchange"/> structure based on the exchange code and description.
        /// </summary>
        /// <param name="code">Exchange code.</param>
        /// <param name="description">Exchange description.</param>
        public Exchange(string code, string description) => (Code, Description) = (code, description);
    }

    /// <summary>
    /// A collection of delegates that <see cref="IOpenfeedClient"/> and <see cref="IOpenfeedConnection"/> will use to notify
    /// the client application about the events that are happening.
    /// </summary>
    public sealed class OpenfeedListeners {
        private readonly Dictionary<long, (InstrumentDefinition?, string[]?)> _instrumentDefinitions = new Dictionary<long, (InstrumentDefinition?, string[]?)>();
        private readonly Dictionary<string, InstrumentDefinition> _instrumentsBySymbol = new Dictionary<string, InstrumentDefinition>();

        /// <summary>
        /// Constructs a new instance of <see cref="OpenfeedListeners"/>.
        /// </summary>
        public OpenfeedListeners() {
            OnMessage = OnAddDetails;
        }


        /// <summary>
        /// Returns the <see cref="InstrumentDefinition"/> based on the Openfeed symbol.
        /// </summary>
        /// <param name="symbol">Openfeed symbol.</param>
        /// <returns><see cref="InstrumentDefinition"/>  or null.</returns>
        public InstrumentDefinition? TryGetInstrumentFromSymbol(string symbol) {
            lock (_instrumentsBySymbol) {
                return _instrumentsBySymbol.TryGetValue(symbol, out var def) ? def : null;
            }
        }

        /// <summary>
        /// Function that will be called when a connection to the websocket fails.
        /// </summary>
        public Func<Exception, ValueTask> OnConnectFailed = ex => default;

        /// <summary>
        /// Function that will be called when the server rejects the credentials with which the <see cref="IOpenfeedClient"/> has been created.
        /// The <see cref="IOpenfeedClient"/> will not attempt any more reconnects after calling this handler.
        /// </summary>
        public Func<ValueTask> OnCredentialsRejected = () => default;

        /// <summary>
        /// Function that will be called when the <see cref="IOpenfeedClient"/> is connected to the server.
        /// </summary>
        public Func<IOpenfeedConnection, ValueTask> OnConnected = connecton => default;

        /// <summary>
        /// Function that will be called when the <see cref="IOpenfeedClient"/> gets disconnected from the server, either because
        /// it was disposed or because the TCP connection broke.
        /// </summary>
        public Func<ValueTask> OnDisconnected = () => default;

        /// <summary>
        /// Function that will be called when a message is received from the server. By default this just adds the instrument definition
        /// and forwards the call to <see cref="OnMessageWithMetadata"/>.
        /// </summary>
        public Func<OpenfeedGatewayMessage, ValueTask> OnMessage;

        /// <summary>
        /// The standard handler for OnMessage looks up the <see cref="InstrumentDefinition"/> and then calls this dialog.
        /// </summary>
        public Func<OpenfeedGatewayMessage, InstrumentDefinition?, string[], ValueTask> OnMessageWithMetadata = (msg, def, symbols) => default;

        private ValueTask OnAddDetails(OpenfeedGatewayMessage msg) {
            (InstrumentDefinition?, string[]?) GetInstrumentDefinition(long marketId) =>
                _instrumentDefinitions.TryGetValue(marketId, out var def) ? def : (null, null);

            InstrumentDefinition? def = null;
            string[]? symbols = null;
            switch (msg.DataCase) {
                case OpenfeedGatewayMessage.DataOneofCase.SubscriptionResponse: {
                    if (msg.SubscriptionResponse.Symbol != null && msg.SubscriptionResponse.MarketId != 0) {
                        (def, symbols) = GetInstrumentDefinition(msg.SubscriptionResponse.MarketId);
                        if (symbols == null) {
                            symbols = new string[1];
                            symbols[0] = msg.SubscriptionResponse.Symbol;
                        }
                        else {
                            if (Array.IndexOf(symbols, msg.SubscriptionResponse.Symbol) < 0) {
                                Array.Resize(ref symbols, symbols.Length + 1);
                                symbols[symbols.Length - 1] = msg.SubscriptionResponse.Symbol;
                            }
                        }
                        _instrumentDefinitions[msg.SubscriptionResponse.MarketId] = (def, symbols);
                    }

                    break;
                }
                case OpenfeedGatewayMessage.DataOneofCase.InstrumentDefinition: {
                    (def, symbols) = GetInstrumentDefinition(msg.InstrumentDefinition.MarketId);
                    _instrumentDefinitions[msg.InstrumentDefinition.MarketId] = (msg.InstrumentDefinition, symbols);
                    lock(_instrumentsBySymbol) {
                        _instrumentsBySymbol[msg.InstrumentDefinition.Symbol] = msg.InstrumentDefinition;
                    }
                    break;
                }
                case OpenfeedGatewayMessage.DataOneofCase.MarketSnapshot: {
                    (def, symbols) = GetInstrumentDefinition(msg.MarketSnapshot.MarketId);
                    break;
                }
                case OpenfeedGatewayMessage.DataOneofCase.MarketUpdate: {
                    (def, symbols) = GetInstrumentDefinition(msg.MarketUpdate.MarketId);
                    break;
                }
                case OpenfeedGatewayMessage.DataOneofCase.Ohlc: {
                    (def, symbols) = GetInstrumentDefinition(msg.Ohlc.MarketId);
                    break;
                }
            }

            return OnMessageWithMetadata(msg, def, symbols ?? Array.Empty<string>());
        }
    }

    /// <summary>
    /// Instance of the client. The client will automatically connect to the specified serve and if the connection breaks it will automatically reconnect.
    /// </summary>
    public interface IOpenfeedClient : IDisposable {
        /// <summary>
        /// Returns a <see cref="ValueTask{IOpenfeedConnection}"/> with the current <see cref="IOpenfeedConnection"/>. The connection represents
        /// the current TCP connection to the Openfeed Websocket server.
        /// </summary>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns><see cref="ValueTask{IOpenfeedConnection}"/> with the current <see cref="IOpenfeedConnection"/>.</returns>
        ValueTask<IOpenfeedConnection> GetConnectionAsync(CancellationToken ct);

        /// <summary>
        /// Sends a <see cref="SubscriptionRequest"/> to the server to which we are currently connected. If the client not currently
        /// connected then waits for the connection to be established and then sends the <see cref="SubscriptionRequest"/>.
        /// If the client gets disconnected then it waits for the reconnect and sends the <see cref="SubscriptionRequest"/>.
        /// </summary>
        /// <param name="service">The <see cref="Service"/> to which to subscribe.</param>
        /// <param name="subscriptionType"><see cref="SubscriptionType"/>.</param>
        /// <param name="snapshotIntervalSeconds">Setting of the cadence at which the snapshots will be sent. If zero the the snapshot is only
        /// sent once.</param>
        /// <param name="symbols">A collection of symbols to which to subscribe, or null if no symbol subscription is to be made.</param>
        /// <param name="marketIds">A collection of market ID's to which to subscribe, or null if no subscription by market ID's is to be made.</param>
        /// <param name="exchanges">A collection of exchanges to which to subscribe, or null if no subscription by exchange is to be made.</param>
        /// <param name="channels">A collection of channels to which to subscribe, or null if no subscription by channel is to be made.</param>
        /// <returns>The ID of the subscription which can be used in a call to <see cref="IOpenfeedClient.Unsubscribe(long)"/> to terminate the subscription.</returns>
        long Subscribe(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, IEnumerable<string>? symbols = null, IEnumerable<long>? marketIds = null, IEnumerable<string>? exchanges = null, IEnumerable<int>? channels = null);

        /// <summary>
        /// Unsubscribes from the feed for a given <paramref name="subscriptionId"/>.
        /// </summary>
        /// <param name="subscriptionId">Subscription ID obtained from a call to <see cref="IOpenfeedClient.Subscribe(Service, SubscriptionType, int, IEnumerable{string}, IEnumerable{long}, IEnumerable{string}, IEnumerable{int})"/></param>
        void Unsubscribe(long subscriptionId);
    }

    /// <summary>
    /// Represents an instance of a TCP connection to the server. Valid until a disconnect.
    /// </summary>
    public interface IOpenfeedConnection {
        /// <summary>
        /// Gets the list of exchanges. In case the <see cref="IOpenfeedConnection"/> gets disconnected it will throw <see cref="OpenfeedDisconnectedException"/>.
        /// </summary>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="IReadOnlyList{Exchange}"/></returns>
        ValueTask<IReadOnlyList<Exchange>> GetExchangesAsync(CancellationToken ct);

        /// <summary>
        /// Sends an <see cref="InstrumentRequest"/> and returns an <see cref="InstrumentResponse"/> or throws an <see cref="OpenfeedDisconnectedException"/>. The
        /// individual <see cref="InstrumentDefinition"/> responses will be sent to <see cref="OpenfeedListeners.OnMessage"/> delegates.
        /// </summary>
        /// <param name="request">The request to be sent.</param>
        /// <param name="ct"><see cref="CancellationToken"/></param>
        /// <returns>A task that will return an <see cref="InstrumentResponse"/> or throw an
        /// <see cref="OpenfeedDisconnectedException"/> if the connection disconnects.</returns>
        Task<InstrumentResponse> GetInstrumentAsync(InstrumentRequest request, CancellationToken ct);

        /// <summary>
        /// Sends an <see cref="InstrumentReferenceRequest"/> and returns the first <see cref="InstrumentReferenceResponse"/>
        /// that the server returns.
        /// </summary>
        /// <param name="request"><see cref="InstrumentReferenceRequest"/></param>
        /// <param name="ct"><see cref="CancellationToken"/></param>
        /// <returns>A task with the first <see cref="InstrumentReferenceResponse"/> received or throws an <see cref="OpenfeedDisconnectedException"/> if the
        /// connection gets broken.</returns>
        Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(InstrumentReferenceRequest request, CancellationToken ct);

        /// <summary>
        /// Sends a <see cref="SubscriptionRequest"/> to the server to which we are currently connected. If the connection is no longer
        /// connected throws a <see cref="OpenfeedDisconnectedException"/>.
        /// </summary>
        /// <param name="service">The <see cref="Service"/> to which to subscribe.</param>
        /// <param name="subscriptionType"><see cref="SubscriptionType"/>.</param>
        /// <param name="snapshotIntervalSeconds">Setting of the cadence at which the snapshots will be sent. If zero the the snapshot is only
        /// sent once.</param>
        /// <param name="symbols">A collection of symbols to which to subscribe, or null if no symbol subscription is to be made.</param>
        /// <param name="marketIds">A collection of market ID's to which to subscribe, or null if no subscription by market ID's is to be made.</param>
        /// <param name="exchanges">A collection of exchanges to which to subscribe, or null if no subscription by exchange is to be made.</param>
        /// <param name="channels">A collection of channels to which to subscribe, or null if no subscription by channel is to be made.</param>
        /// <returns>The ID of the subscription which can be used in a call to <see cref="IOpenfeedConnection.Unsubscribe(long)"/> to terminate the subscription.</returns>
        long Subscribe(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, IEnumerable<string>? symbols = null, IEnumerable<long>? marketIds = null, IEnumerable<string>? exchanges = null, IEnumerable<int>? channels = null);

        /// <summary>
        /// Sends the <see cref="SubscriptionRequest"/> to the server with <see cref="SubscriptionRequest.Unsubscribe"/> set to true.
        /// </summary>
        /// <param name="id">ID of the subscription request returned by the call to <see cref="IOpenfeedConnection.Subscribe(Service, SubscriptionType, int, IEnumerable{string}, IEnumerable{long}, IEnumerable{string}, IEnumerable{int})"/>.</param>
        void Unsubscribe(long id);

        /// <summary>
        /// Gets the task that will be signalled when the connection instance gets disconnected from the server.
        /// </summary>
        /// <param name="ct"><see cref="CancellationToken"/></param>
        /// <returns>A task that will be signalled when the connection instance is disconnected from the server.</returns>
        Task WhenDisconnectedAsync(CancellationToken ct);
    }

    /// <summary>
    /// Extension methods with shorthands for common Openfeed tasks.
    /// </summary>
    public static class OpenfeedExtensions {
        /// <summary>
        /// Gets the list of exchanges.
        /// </summary>
        /// <param name="client"><see cref="IOpenfeedClient"/></param>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns>The <see cref="IReadOnlyList{Exchange}"/></returns>
        public static async ValueTask<IReadOnlyList<Exchange>> GetExchangesAsync(this IOpenfeedClient client, CancellationToken ct) {
            for (; ; ) {
                try {
                    var connection = await client.GetConnectionAsync(ct).ConfigureAwait(false);
                    return await connection.GetExchangesAsync(ct).ConfigureAwait(false);
                }
                catch (OpenfeedDisconnectedException) {
                }
            }
        }

        /// <summary>
        /// Gets the list of instrument references for a symbol.
        /// </summary>
        /// <param name="connection"><see cref="IOpenfeedConnection"/>.</param>
        /// <param name="symbol">Symbol for which to obtain the <see cref="InstrumentReferenceResponse"/>.</param>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns><see cref="InstrumentReferenceResponse"/>.</returns>
        public static Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(this IOpenfeedConnection connection, string symbol, CancellationToken ct) =>
            connection.GetInstrumentReferenceAsync(new InstrumentReferenceRequest { Symbol = symbol }, ct);

        /// <summary>
        /// Gets the list of instrument references for a symbol.
        /// </summary>
        /// <param name="connection"><see cref="IOpenfeedConnection"/>.</param>
        /// <param name="marketId">Market ID for which to obtain the <see cref="InstrumentReferenceResponse"/>.</param>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns><see cref="InstrumentReferenceResponse"/>.</returns>
        public static Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(this IOpenfeedConnection connection, long marketId, CancellationToken ct) =>
            connection.GetInstrumentReferenceAsync(new InstrumentReferenceRequest { MarketId = marketId }, ct);

        /// <summary>
        /// Gets the list of instrument references for a symbol.
        /// </summary>
        /// <param name="client"><see cref="IOpenfeedClient"/>.</param>
        /// <param name="symbol">Symbol for which to obtain the <see cref="InstrumentReferenceResponse"/>.</param>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns><see cref="InstrumentReferenceResponse"/>.</returns>
        public static async Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(this IOpenfeedClient client, string symbol, CancellationToken ct) {
            for (; ; ) {
                try {
                    var connection = await client.GetConnectionAsync(ct).ConfigureAwait(false);
                    return await connection.GetInstrumentReferenceAsync(new InstrumentReferenceRequest { Symbol = symbol }, ct);
                }
                catch (OpenfeedDisconnectedException) {
                }
            }
        }

        /// <summary>
        /// Gets the list of instrument references for a symbol.
        /// </summary>
        /// <param name="client"><see cref="IOpenfeedClient"/>.</param>
        /// <param name="marketId">Market ID for which to obtain the <see cref="InstrumentReferenceResponse"/>.</param>
        /// <param name="ct"><see cref="CancellationToken"/>.</param>
        /// <returns><see cref="InstrumentReferenceResponse"/>.</returns>
        public static async Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(this IOpenfeedClient client, long marketId, CancellationToken ct) {
            for (; ; ) {
                try {
                    var connection = await client.GetConnectionAsync(ct).ConfigureAwait(false);
                    return await connection.GetInstrumentReferenceAsync(new InstrumentReferenceRequest { MarketId = marketId }, ct);
                }
                catch (OpenfeedDisconnectedException) {
                }
            }
        }
    }
}
