using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Org.Openfeed.Client {

    public class OpenfeedRequestException : Exception {
        public readonly Result Result;

        public OpenfeedRequestException(Status status) : base(status.Message) {
            Result = status.Result;
        }

        public static void ThrowOnError(Status status) {
            if (status.Result != Result.Success) throw new OpenfeedRequestException(status);
        }
    }

    public class OpenfeedDisconnectedException : Exception {
        public OpenfeedDisconnectedException() : base("Disconnected from the server.") { }
    }

    public readonly struct Exchange {
        public readonly string Code, Description;

        public Exchange(string code, string description) => (Code, Description) = (code, description);
    }

    public sealed class OpenfeedListeners {
        public Func<Exception, ValueTask> OnConnectFailed = ex => default;
        public Func<ValueTask> OnCredentialsRejected = () => default;
        public Func<IOpenfeedConnection, ValueTask> OnConnected = connecton => default;
        public Func<ValueTask> OnDisconnected = () => default;

        public Func<OpenfeedGatewayMessage, ValueTask> OnMessage = msg => default;
    }

    public interface IOpenfeedClient : IDisposable {
        ValueTask<IOpenfeedConnection> GetConnectionAsync(CancellationToken ct);

        long Subscribe(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, IEnumerable<string>? symbols = null, IEnumerable<long>? marketIds = null, IEnumerable<string>? exchanges = null, IEnumerable<int>? channels = null);
        void Unsubscribe(long subscriptionId);
    }

    public interface IOpenfeedConnection {
        ValueTask<IReadOnlyList<Exchange>> GetExchangesAsync(CancellationToken ct);
        Task<InstrumentResponse> GetInstrumentAsync(InstrumentRequest request, CancellationToken ct);
        Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(InstrumentReferenceRequest request, CancellationToken ct);

        long Subscribe(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, IEnumerable<string>? symbols = null, IEnumerable<long>? marketIds = null, IEnumerable<string>? exchanges = null, IEnumerable<int>? channels = null);
        void Unsubscribe(long id);

        Task Disconnected { get; }
    }

    public static class OpenfeedExtensions {
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

        public static Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(this IOpenfeedConnection connection, string symbol, CancellationToken ct) =>
            connection.GetInstrumentReferenceAsync(new InstrumentReferenceRequest { Symbol = symbol }, ct);

        public static Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(this IOpenfeedConnection connection, long marketId, CancellationToken ct) =>
            connection.GetInstrumentReferenceAsync(new InstrumentReferenceRequest { MarketId = marketId }, ct);

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
