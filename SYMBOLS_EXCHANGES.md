# Symbols and Exchanges

The IOpenfeedClient.Subscribe method allows you to subscribe to individual symbols and exchanges, depending on your needs.

Subscriptions by symbol are the most lightweight as they only subscribe to a few symbols at a time. Subscriptions by exchanges will subscribe to everything on a single exchange at once.

To subscribe to just a few symbols, all you need is to pass the list of symbols in the Subscribe method symbols argument:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: new[] { "MSFT", "GOOG" });
```

To subscribe to a data coming from an exchange wholesale, pass the list of exchanges you are interested in in the call to Subscribe:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.All, 1, exchanges: new[] { "CME" });
```
