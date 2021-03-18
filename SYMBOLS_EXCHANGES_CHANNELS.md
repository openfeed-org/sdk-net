# Symbols, Exchanges and Channels

The IOpenfeedClient.Subscribe method allows you to subscribe to individual symbols, channels and exchanges, depending on your needs.

Subscriptions by symbol are the most lightweight as they only subscribe to a few symbols at a time. Subscriptions by exchanges or channels will subscribe to everything on a single exchange or a channel at once.

To subscribe to just a few symbols, all you need is to pass the list of symbols in the Subscribe method symbols argument:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: new[] { "MSFT", "GOOG" });
```

To subscribe to a data coming from an exchange wholesale, pass the list of exchanges you are interested in in the call to Subscribe:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.All, 1, exchanges: new[] { "CME" });
```

To subscribe to all the data coming from, say, channel 1, pass that channel number in the channels argument:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.All, 1, channels: new[] { 1 });
```

The list of channels is available in the [Openfeed documentation](https://openfeed-org.github.io/documentation/#channels-and-feeds).