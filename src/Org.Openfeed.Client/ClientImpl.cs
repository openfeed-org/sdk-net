﻿using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Org.Openfeed.Client {

    static class CorrelationId {
        private static long _next;

        public static long Create() => Interlocked.Increment(ref _next);
    }

    class MessageFramer {
        private byte[] _outputStreamBuffer;
        private MemoryStream _outputStream;

        public MessageFramer() {
            _outputStreamBuffer = new byte[4096];
            _outputStream = new MemoryStream(_outputStreamBuffer);
        }

        public async ValueTask SendAsync(ClientWebSocket socket, OpenfeedGatewayRequest request, CancellationToken ct) {
            var size = request.CalculateSize();
            if (size > _outputStream.Capacity) {
                _outputStreamBuffer = new byte[size];
                _outputStream = new MemoryStream(_outputStreamBuffer);
            }
            else {
                _outputStream.Position = 0;
            }
            request.WriteTo(_outputStream);
            _outputStream.Flush();

            await socket.SendAsync(new ArraySegment<byte>(_outputStreamBuffer, 0, size), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }

        private byte[] _inputBuffer = new byte[4096];

        public async ValueTask<OpenfeedGatewayMessage> ReceiveAsync(ClientWebSocket socket, CancellationToken ct) {
            int messageLength = 0;
            WebSocketMessageType messageType;
            for (; ; ) {
                var receiveResult = await socket.ReceiveAsync(new ArraySegment<byte>(_inputBuffer, messageLength, _inputBuffer.Length - messageLength), ct).ConfigureAwait(false);
                if (receiveResult.CloseStatus != null || receiveResult.MessageType == WebSocketMessageType.Close) throw new Exception($"WebSocket closed {receiveResult.CloseStatus}: {receiveResult.CloseStatusDescription}");
                messageLength += receiveResult.Count;

                if (receiveResult.EndOfMessage) {
                    messageType = receiveResult.MessageType;
                    break;
                }

                if (messageLength == _inputBuffer.Length) {
                    Array.Resize(ref _inputBuffer, _inputBuffer.Length * 2);
                }
            }

            if (messageType == WebSocketMessageType.Text) {
                var json = Encoding.UTF8.GetString(_inputBuffer, 0, messageLength);
                return OpenfeedGatewayMessage.Parser.ParseJson(json);
            }
            else {
                return OpenfeedGatewayMessage.Parser.ParseFrom(_inputBuffer, 0, messageLength);
            }
        }
    }

    class Client : IOpenfeedClient {
        private readonly Uri _uri;
        private readonly string _username, _password;
        private readonly OpenfeedListeners _listeners;
        private readonly CancellationTokenSource _disposedSource = new CancellationTokenSource();

        private readonly MessageFramer _messageFramer = new MessageFramer();

        private object _currentConnectionLock = new object();
        private readonly List<TaskCompletionSource<ConnectionImpl>> _currentConnectionWaiters = new List<TaskCompletionSource<ConnectionImpl>>();
        private ConnectionImpl? _currentConnection;

        private enum RequestType {
            InstrumentRequest,
            InstrumentReferenceRequest,
            ExchangeRequest
        }

        public Client(Uri uri, string username, string password, OpenfeedListeners listeners) {
            _uri = uri;
            _username = username;
            _password = password;
            _listeners = listeners;

            RunConnectionLoop();
        }

        public void Dispose() => _disposedSource.Cancel();

        private async void RunConnectionLoop() {
            await ContinueOnThreadPool.Instance;

            var ct = _disposedSource.Token;

            for (; ; ) {
                try {
                    bool authenticationFailed = await ConnectAndShuffleMessages().ConfigureAwait(false);

                    if (authenticationFailed) {
                        Trace.TraceInformation($"Authentication failed.");
                        break;
                    }
                }
                catch (Exception) when (ct.IsCancellationRequested) {
                    break;
                }
                catch (WebSocketException e) {
                    Trace.TraceInformation($"WebSocket connection to {_uri} failed with {e}");
                }
                catch (Exception e) {
                    Trace.TraceInformation($"WebSocket connection to {_uri} failed with {e}");
                }

                try {
                    await Task.Delay(5_000, ct);
                }
                catch (Exception) when (ct.IsCancellationRequested) {
                    break;
                }
            }

            lock (_currentConnectionLock) {
                var disp = new ObjectDisposedException("Openfeed Client");
                foreach (var x in _currentConnectionWaiters) {
                    x.SetException(disp);
                }
            }
        }

        private async Task<(bool AuthenticationFailed, string Token)> LoginAsync(ClientWebSocket socket, MessageFramer messageFramer) {
            var ct = _disposedSource.Token;

            var loginRequest = new OpenfeedGatewayRequest { LoginRequest = new LoginRequest { CorrelationId = 0, Username = _username, Password = _password } };
            await messageFramer.SendAsync(socket, loginRequest, ct).ConfigureAwait(false);

            var loginResponse = (await messageFramer.ReceiveAsync(socket, ct).ConfigureAwait(false)).LoginResponse;
            if (loginResponse == null) throw new InvalidDataException("Expected a LoginResponse message in response to our LoginRequest.");
            if (loginResponse.CorrelationId != 0) throw new InvalidDataException($"Received LoginResponse message has an incorrect correlation ID. Expected 0, received {loginResponse.CorrelationId}.");

            var token = loginResponse.Token;

            switch (loginResponse.Status.Result) {
                case Result.Success: break;
                case Result.AuthenticationRequired:
                case Result.InvalidCredentials:
                case Result.InsufficientPrivileges: {
                    return (true, token);
                }
                default: throw new InvalidDataException($"Login failed with result {loginResponse.Status.Result}: {loginResponse.Status.Message}.");
            }

            return (false, token);
        }

        // Returns true if the login failed.
        private async Task<bool> ConnectAndShuffleMessages() {
            var ct = _disposedSource.Token;

            using (var socket = new ClientWebSocket()) {
                socket.Options.UseDefaultCredentials = true;
                var proxy = WebRequest.GetSystemWebProxy();
                if (proxy != null) {
                    socket.Options.Proxy = proxy;
                }
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

                Trace.TraceInformation($"WebSocket connecting to {_uri}...");
                try {
                    await socket.ConnectAsync(_uri, ct).ConfigureAwait(false);
                }
                catch (Exception e) {
                    Trace.TraceWarning($"WebSocket connection to {_uri} failed with exception: {e.ToString()}");
                    await _listeners.OnConnectFailed(e);
                    return false;
                }

                Trace.TraceInformation($"WebSocket connected to {_uri}, logging in...");
                var (loginFailed, token) = await LoginAsync(socket, _messageFramer).ConfigureAwait(false);
                if (loginFailed) {
                    Trace.TraceError($"WebSocket connected to {_uri}, log in failed.");
                    await _listeners.OnCredentialsRejected().ConfigureAwait(false);
                    return true;
                }
                else {
                    Trace.TraceInformation($"WebSocket connected to {_uri}, logged in.");

                    var connection = new ConnectionImpl(token, _listeners.OnMessage, ct);
                    await _listeners.OnConnected(connection).ConfigureAwait(false);

                    lock (_currentConnectionLock) {
                        _currentConnection = connection;
                        foreach (var x in _currentConnectionWaiters) {
                            x.SetResult(connection);
                        }
                        _currentConnectionWaiters.Clear();
                    }
                    try {
                        await connection.RunSocketLoop(socket, _messageFramer).ConfigureAwait(false);
                    }
                    finally {
                        lock (_currentConnectionLock) {
                            Debug.Assert(_currentConnectionWaiters.Count == 0);
                            _currentConnection = null;
                        }

                        await _listeners.OnDisconnected().ConfigureAwait(false);
                    }

                    return false;
                }
            }
        }

        public async ValueTask<IOpenfeedConnection> GetConnectionAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            TaskCompletionSource<ConnectionImpl> retSource;
            lock (_currentConnectionLock) {
                if (_currentConnection != null) {
                    return _currentConnection;
                }
                else {
                    retSource = new TaskCompletionSource<ConnectionImpl>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _currentConnectionWaiters.Add(retSource);
                }
            }

            using (var caw = new CancellationAwaiter(ct, false)) {
                await Task.WhenAny(retSource.Task, caw.Task).ConfigureAwait(false);
            }

            lock (_currentConnectionLock) {
                _currentConnectionWaiters.Remove(retSource);
            }

            ct.ThrowIfCancellationRequested();

            return await retSource.Task.ConfigureAwait(false);
        }

        private readonly Dictionary<long, CancellationTokenSource> _subscriptions = new Dictionary<long, CancellationTokenSource>();

        private async void RunSubscribeLoop(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, List<string>? symbols, List<long>? marketIds, List<string>? exchanges, List<int>? channels, CancellationToken ct) {
            var combined = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposedSource.Token).Token;

            if (combined.IsCancellationRequested) return;

            for (; ; ) {
                IOpenfeedConnection connection;
                try {
                    connection = await GetConnectionAsync(combined).ConfigureAwait(false);
                }
                catch {
                    continue;
                }

                long? subscriptionId = null;
                try {
                    subscriptionId = connection.Subscribe(service, subscriptionType, snapshotIntervalSeconds, symbols, marketIds, exchanges, channels);
                    await connection.WhenDisconnectedAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                    if (subscriptionId != null) {
                        connection.Unsubscribe(subscriptionId.Value);
                    }
                    break;
                }
                catch (OpenfeedDisconnectedException) {
                }
            }
        }

        public long Subscribe(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, IEnumerable<string>? symbols, IEnumerable<long>? marketIds, IEnumerable<string>? exchanges, IEnumerable<int>? channels) {
            long id = CorrelationId.Create();
            var cts = new CancellationTokenSource();

            lock (_subscriptions) {
                _subscriptions.Add(id, cts);
            }

            RunSubscribeLoop(service, subscriptionType, snapshotIntervalSeconds, symbols?.ToList(), marketIds?.ToList(), exchanges?.ToList(), channels?.ToList(), cts.Token);

            return id;
        }

        public void Unsubscribe(long subscriptionId) {
            CancellationTokenSource cts;

            lock (_subscriptions) {
                if (!_subscriptions.TryGetValue(subscriptionId, out cts)) throw new ArgumentException($"Subscription with id {subscriptionId} does not exist.", nameof(subscriptionId));
                _subscriptions.Remove(subscriptionId);
            }

            cts.Cancel();
        }
    }

    class ConnectionImpl : IOpenfeedConnection {
        private readonly string _token;
        private readonly CancellationToken _disposedToken;
        private readonly Func<OpenfeedGatewayMessage, ValueTask> _onMessage;
        private readonly List<TaskCompletionSource<bool>> _disconnectWaiters = new List<TaskCompletionSource<bool>>();

        private bool _disconnected;

        public ConnectionImpl(string connectionToken, Func<OpenfeedGatewayMessage, ValueTask> onMessage, CancellationToken cancellationToken) {
            _token = connectionToken;
            _disposedToken = cancellationToken;
            _onMessage = onMessage;
        }

        private readonly object _lock = new object();
        private List<OpenfeedGatewayRequest> _pendingRequests = new List<OpenfeedGatewayRequest>();
        private TaskCompletionSource<bool> _hasPendingRequests = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // request bookkeeping

        private readonly struct RequestData<T> {
            public readonly TaskCompletionSource<T> ResultSlot;
            public readonly CancellationTokenRegistration CancellationRegistration;

            public RequestData(TaskCompletionSource<T> resultSlot, CancellationTokenRegistration cancellationRegistration) =>
                (ResultSlot, CancellationRegistration) = (resultSlot, cancellationRegistration);

            public readonly void Cancel() {
                CancellationRegistration.Dispose();
                ResultSlot.SetCanceled();
            }
        }

        private readonly Dictionary<long, RequestData<ExchangeResponse>> _exchangeRequests = new Dictionary<long, RequestData<ExchangeResponse>>();
        private readonly Dictionary<long, RequestData<InstrumentResponse>> _instrumentRequests = new Dictionary<long, RequestData<InstrumentResponse>>();
        private readonly Dictionary<long, RequestData<InstrumentReferenceResponse>> _instrumentReferenceRequests = new Dictionary<long, RequestData<InstrumentReferenceResponse>>();

        private readonly Dictionary<long, SubscriptionRequest> _subscriptions = new Dictionary<long, SubscriptionRequest>();

        // communication

        public async Task RunSocketLoop (ClientWebSocket socket, MessageFramer messageFramer) {
            var requests = new List<OpenfeedGatewayRequest>();

            var receiveTask = messageFramer.ReceiveAsync(socket, _disposedToken);
            Task pendingRequestTask;
            lock (_lock) {
                pendingRequestTask = _hasPendingRequests.Task;
            }
            Task<OpenfeedGatewayMessage>? receiveTaskTask = null;

            try {
                for (; ; ) {
                    bool hasData = (receiveTaskTask != null ? receiveTaskTask.IsCompleted : receiveTask.IsCompleted) || pendingRequestTask.IsCompleted;
                    if (!hasData) {
                        if (receiveTaskTask == null) receiveTaskTask = receiveTask.AsTask();
                        await Task.WhenAny(receiveTaskTask, pendingRequestTask).ConfigureAwait(false);
                    }

                    if (pendingRequestTask.IsCompleted) {
                        lock (_lock) {
                            requests.Clear();
                            (requests, _pendingRequests) = (_pendingRequests, requests);
                            _hasPendingRequests = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                            pendingRequestTask = _hasPendingRequests.Task;
                        }

                        foreach (var req in requests) {
                            await messageFramer.SendAsync(socket, req, _disposedToken).ConfigureAwait(false);
                        }

                        requests.Clear();
                    }

                    if (receiveTaskTask != null) {
                        if (receiveTaskTask.IsCompleted) {
                            var msg = await receiveTaskTask.ConfigureAwait(false);
                            await DispatchMessage(msg);
                            receiveTask = messageFramer.ReceiveAsync(socket, _disposedToken);
                            receiveTaskTask = null;
                        }
                    }
                    else if (receiveTask.IsCompleted) {
                        var msg = await receiveTask.ConfigureAwait(false);
                        await DispatchMessage(msg);
                        receiveTask = messageFramer.ReceiveAsync(socket, _disposedToken);
                    }
                }
            }
            finally {
                Exception ex = new OpenfeedDisconnectedException();

                lock (_lock) {
                    _disconnected = true;

                    void CancelOutstandingRequests<T> (Dictionary<long, RequestData<T>> dict) {
                        foreach (var data in dict.Values) {
                            data.CancellationRegistration.Dispose();
                            if (_disposedToken.IsCancellationRequested) {
                                data.ResultSlot.SetCanceled();
                            }
                            else {
                                data.ResultSlot.SetException(ex);
                            }
                        }

                        dict.Clear();
                    }

                    CancelOutstandingRequests(_exchangeRequests);
                    CancelOutstandingRequests(_instrumentRequests);
                    CancelOutstandingRequests(_instrumentReferenceRequests);

                    _subscriptions.Clear();

                    foreach (var x in _disconnectWaiters) {
                        x.SetResult(true);
                    }
                }
            }
        }

        private async ValueTask<bool> DispatchMessage(OpenfeedGatewayMessage msg) {
            void DispatchResponseResult<T> (long correlationId, T response, Dictionary<long, RequestData<T>> dict) {
                lock (_lock) {
                    if (dict.TryGetValue(correlationId, out var data)) {
                        dict.Remove(correlationId);
                        data.CancellationRegistration.Dispose();
                        data.ResultSlot.SetResult(response);
                    }
                }
            }

            switch (msg.DataCase) {
                case OpenfeedGatewayMessage.DataOneofCase.HeartBeat: {
                    return true;
                }
                case OpenfeedGatewayMessage.DataOneofCase.InstrumentResponse: {
                    await _onMessage(msg).ConfigureAwait(false);
                    var resp = msg.InstrumentResponse;
                    if (resp != null) DispatchResponseResult(resp.CorrelationId, resp, _instrumentRequests);
                    return true;
                }
                case OpenfeedGatewayMessage.DataOneofCase.InstrumentReferenceResponse: {
                    await _onMessage(msg).ConfigureAwait(false);

                    var resp = msg.InstrumentReferenceResponse;
                    if (resp != null) DispatchResponseResult(resp.CorrelationId, resp, _instrumentReferenceRequests);

                    return true;
                }
                case OpenfeedGatewayMessage.DataOneofCase.ExchangeResponse: {
                    await _onMessage(msg).ConfigureAwait(false);

                    var resp = msg.ExchangeResponse;
                    if (resp != null) DispatchResponseResult(resp.CorrelationId, resp, _exchangeRequests);
                    
                    return true;
                }
                case OpenfeedGatewayMessage.DataOneofCase.LogoutResponse: {
                    return false;
                }
                default: {
                    await _onMessage(msg).ConfigureAwait(false);
                    return true;
                }
            }
        }

        private void QueueRequest(OpenfeedGatewayRequest request) {
            bool wasEmpty = _pendingRequests.Count == 0;
            _pendingRequests.Add(request);
            if (wasEmpty) {
                _hasPendingRequests.SetResult(false);
            }
        }

        private void OnRequestCancelled<T>(Dictionary<long, RequestData<T>> dict, long correlationId)  {
            RequestData<T> record;
            bool found;
            lock (_lock) {
                found = dict.TryGetValue(correlationId, out record);
                if (found) dict.Remove(correlationId);
            }

            if (found) record.Cancel();
        }

        private void OnExchangeRequestCancelled(object obj) => OnRequestCancelled(_exchangeRequests, (long)obj);

        public async ValueTask<IReadOnlyList<Exchange>> GetExchangesAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            _disposedToken.ThrowIfCancellationRequested();

            long correlationId = CorrelationId.Create();
            var req = new ExchangeRequest { CorrelationId = correlationId, Token = _token };
            var tcs = new TaskCompletionSource<ExchangeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock) {
                if (_disconnected) throw new OpenfeedDisconnectedException();

                var reg = ct.Register(OnExchangeRequestCancelled, correlationId, false);
                _exchangeRequests.Add(req.CorrelationId, new RequestData<ExchangeResponse>(tcs, reg));

                QueueRequest(new OpenfeedGatewayRequest { ExchangeRequest = req });
            }

            var response = await tcs.Task.ConfigureAwait(false);
            OpenfeedRequestException.ThrowOnError(response.Status);

            var exch = response.Exchanges;
            var ret = new Exchange[exch.Count];
            for (int x = 0; x < ret.Length; ++x) {
                ret[x] = new Exchange(exch[x].Code, exch[x].Description);
            }

            return ret;
        }

        private void OnInstrumentsRequestCancelled(object obj) => OnRequestCancelled(_instrumentRequests, (long)obj);

        public Task<InstrumentResponse> GetInstrumentAsync(InstrumentRequest request, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            _disposedToken.ThrowIfCancellationRequested();

            long correlationId = CorrelationId.Create();
            request.CorrelationId = correlationId;
            request.Token = _token;

            var tcs = new TaskCompletionSource<InstrumentResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock) {
                if (_disconnected) throw new OpenfeedDisconnectedException();

                var reg = ct.Register(OnInstrumentsRequestCancelled, correlationId, false);
                _instrumentRequests.Add(correlationId, new RequestData<InstrumentResponse>(tcs, reg));

                QueueRequest(new OpenfeedGatewayRequest { InstrumentRequest = request });
            }

            return tcs.Task;
        }

        private void OnInstrumentReferenceRequestCancelled(object obj) =>
            OnRequestCancelled(_instrumentReferenceRequests, (long)obj);

        public Task<InstrumentReferenceResponse> GetInstrumentReferenceAsync(InstrumentReferenceRequest request, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            _disposedToken.ThrowIfCancellationRequested();

            long correlationId = CorrelationId.Create();

            request.CorrelationId = correlationId;
            request.Token = _token;

            var tcs = new TaskCompletionSource<InstrumentReferenceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_lock) {
                if (_disconnected) throw new OpenfeedDisconnectedException();

                var reg = ct.Register(OnInstrumentReferenceRequestCancelled, correlationId, false);
                _instrumentReferenceRequests.Add(correlationId, new RequestData<InstrumentReferenceResponse>(tcs, reg));

                QueueRequest(new OpenfeedGatewayRequest { InstrumentReferenceRequest = request });
            }

            return tcs.Task;
        }

        public long Subscribe(Service service, SubscriptionType subscriptionType, int snapshotIntervalSeconds, IEnumerable<string>? symbols, IEnumerable<long>? marketIds, IEnumerable<string>? exchanges, IEnumerable<int>? channels) {
            _disposedToken.ThrowIfCancellationRequested();

            long correlationId = CorrelationId.Create();

            var subReq = new SubscriptionRequest { Service = service, CorrelationId = correlationId, Token = _token };
            if (symbols != null) {
                foreach (var symbol in symbols) {
                    var req = new SubscriptionRequest.Types.Request { Symbol = symbol, SnapshotIntervalSeconds = snapshotIntervalSeconds };
                    req.SubscriptionType.Add(subscriptionType);
                    subReq.Requests.Add(req);
                }
            }
            if (marketIds != null) {
                foreach (var marketId in marketIds) {
                    var req = new SubscriptionRequest.Types.Request { MarketId = marketId, SnapshotIntervalSeconds = snapshotIntervalSeconds };
                    req.SubscriptionType.Add(subscriptionType);
                    subReq.Requests.Add(req);
                }
            }
            if (exchanges != null) {
                foreach (var exchange in exchanges) {
                    var req = new SubscriptionRequest.Types.Request { Exchange = exchange, SnapshotIntervalSeconds = snapshotIntervalSeconds };
                    req.SubscriptionType.Add(subscriptionType);
                    subReq.Requests.Add(req);
                }
            }
            if (channels != null) {
                foreach (var channel in channels) {
                    var req = new SubscriptionRequest.Types.Request { ChannelId = channel, SnapshotIntervalSeconds = snapshotIntervalSeconds };
                    req.SubscriptionType.Add(subscriptionType);
                    subReq.Requests.Add(req);
                }
            }

            lock (_lock) {
                if (_disconnected) throw new OpenfeedDisconnectedException();
                _subscriptions.Add(correlationId, subReq);
                QueueRequest(new OpenfeedGatewayRequest { SubscriptionRequest = subReq });
            }

            return correlationId;
        }

        public void Unsubscribe(long subscriptionId) {
            var req = new SubscriptionRequest { CorrelationId = subscriptionId, Token = _token, Unsubscribe = true };

            lock (_lock) {
                if (!_subscriptions.Remove(subscriptionId)) throw new ArgumentException($"Subscription ID {subscriptionId} does not exist.");
                if (!_disconnected) QueueRequest(new OpenfeedGatewayRequest { SubscriptionRequest = req });
            }
        }

        public async Task WhenDisconnectedAsync(CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            TaskCompletionSource<bool> retSource;

            lock (_lock) {
                if (_disconnected) return;
                retSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _disconnectWaiters.Add(retSource);
            }

            using (var caw = new CancellationAwaiter(ct, false)) {
                await Task.WhenAny(retSource.Task, caw.Task).ConfigureAwait(false);
            }

            lock (_lock) {
                _disconnectWaiters.Remove(retSource);
            }

            ct.ThrowIfCancellationRequested();
        }
    }
}
