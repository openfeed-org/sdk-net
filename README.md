# .NET SDK for Barchart OpenFeed

The .NET SDK for Barchart Openfeed is a library that can be used to subscribe to market data messages served by the Barchart [Openfeed](https://openfeed.com/) servers. The documentation about the Openfeed protocol is available [here](https://openfeed-org.github.io/documentation/).

## Obtaining the Library

The easiest way to get started is to add the openfeed.net package from NuGet. The latest version is 1.0.1.

## This Repository

This repository contains a solution with three projects:

1. Org.Openfeed.Client is the source code of the openfeed.net library.
2. Org.Openfeed.Client.Demo is the demo project which demonstrates the use of the above library.
3. Org.Openfeed.Messages is the source code of the project containing the Openfeed message definitions. It has been auto-generated from the Openfeed protocol buffer message definitions.

A good way to learn about using openfeed.net is to clone the repository and poke around the demo source code.

## Core Concepts

There are only two objects that you will need in order to work with Openfeed: a connection client(represented by the IOpenFeedClient interface) and a listener.

The connecton client object will connect to the Openfeed servers and maintain that connection until disposed. It can be used to send requests to subscribe to symbols, exchanges and channels.

The listener object (represented by the OpenfeedListener class) contains five callback delegates that you can wire to your own callback functions in order to process messages coming from the connection.

## The Listener

The listener object, represented by the OpenfeedListener class is dead-simple, just five variables, that point to functions that handle the connected, disconnected, connect failed, credentials rejected and new message events sent from the connection client.

```C#
public sealed class OpenfeedListeners {
    public Func<Exception, ValueTask> OnConnectFailed = ex => default;
    public Func<ValueTask> OnCredentialsRejected = () => default;
    public Func<IOpenfeedConnection, ValueTask> OnConnected = connecton => default;
    public Func<ValueTask> OnDisconnected = () => default;
    public Func<OpenfeedGatewayMessage, ValueTask> OnMessage = msg => default;
}
```

Each of the delegates contained in the listener returns a ValueTask. This allows your callback functions to be asynchronous if needed, and if they are not, like in our example, they should simply return a new ValueTask() to indicate that processing was performed synchronously.

Here is an example of how you might implement a super-simple listener that simply outputs what's happening to the console:

```C#
var listeners = new OpenfeedListeners();

listeners.OnConnected = connection => {
    Console.WriteLine("Connected.");
    return default;
};
listeners.OnDisconnected = () => {
    Console.WriteLine("Disconnected.");
    return default;
};
listeners.OnMessage = msg => {
    Console.WriteLine(msg.ToString());
    return default;
};
listeners.OnConnectFailed = ex => {
    Console.WriteLine(ex);
    return default;
};
listeners.OnCredentialsRejected = () => {
    Console.WriteLine("Credentials rejected.");
    return default;
};
```
The first callback we wired was OnConnected. It simply outputs "Connected." to the console. It then returns a default ValueTask (same as saying 'return new ValueTask') to signal that it has completed its work synchronously.

The OnDisconnected callback is similar, it simply outputs "Disconnected.".

The OnMessage callback will be called when a new message is received from the OpenFeed servers. In this case it simply prints the message to the console, and again returns just a default ValueTask.

The OnConnectFailed callback will be called when a connection attempt to the Openfeed servers fails. This typically happens when there are network problems, and in rarer cases, when the certificate negotiation fails for the secure connection.

The last callback we wired is OnCredentialsRejected, which will be called if you attempt to connect with wrong credentials. This is a terminal state - if you receive this callbacks no other callbacks will be called and no other connection attempts will be made. The only sensible thing to do in this case is to dispose the connection client object and then create a new one with the correct credentials.

It is OK not to implement all these callbacks as they all have an existing null implementation.

### Asynchronous Handlers

Callbacks can also be implemented as asynchronous handlers. Say you are outputting all the messages to a file instead of printing them to a console. The OnMessage callback could then look like this:

```C#
var textFile = File.CreateText("messages.txt");

listeners.OnMessage = async msg => {
    await textFile.WriteLineAsync(msg.ToString());
};
```

## Connection Client

The connection client is represented by the IOpenfeedClient interface:

```C#
public interface IOpenfeedClient : IDisposable {
	long Subscribe(
		Service service,
		SubscriptionType subscriptionType,
		int snapshotIntervalSeconds,
		IEnumerable<string>? symbols = null,
		IEnumerable<long>? marketIds = null,
		IEnumerable<string>? exchanges = null,
		IEnumerable<int>? channels = null
	);

    void Unsubscribe(long subscriptionId);
}
```

To create a new IOpenfeedClient object you can use the static OpenfeedFactory class to create a new instance:

```C#
using var client = OpenfeedFactory.CreateClient(new Uri("ws://openfeed.aws.barchart.com/ws"), username, password, listeners);
```
The first argument is the URL of the Openfeed server, which is typically "ws://openfeed.aws.barchart.com/ws". The second argument is the username, the third is the password, and the fourth is the listeners object which we learned to set up in the previous section.

As soon as you create this object it will attempt to connect to the Openfeed servers and issue the necessary callbacks to the listener.

When you are done with this object simply Dispose it and it will disconnect and stop calling the callback listeners.

Once the object is created, you can simply subscribe to what you need and the listener's OnMessage will be called with the received messages as they arrive. For example:

```C#
var subId = client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: new[] { "MSFT" });
```

This subscribes to all message types from the real-time service, for MSFT. The number 1 represents a snapshot interval, but as in this case we are subscribing to real-time data and not periodic snapshots it's unused.

The call to Subscribe returns a subscription ID, which you can use to later unsubscribe by calling the Unsubscribe function.

The available services are:

```C#
public enum Service {
    RealTime,
    Delayed,
    RealTimeSnapshot,
    DelayedSnapshot
}
```

The available subscription types are:

```C#
public enum SubscriptionType {
    All,
    Quote,
    QuoteParticipant,
    DepthPrice,
    DepthOrder,
    Trades,
    CumlativeVolume,
    Ohlc,
}
```

## Putting it all together

Now that we know how to use the listener and client objects, let's put together a little demo that subscribes to all MSFT messages and prints what's happening to the console:

```C#
using System;
using System.Threading;
using Org.Openfeed;
using Org.Openfeed.Client;

class Program {
    static void Main() {
        
        // Set up the listener
        
        var listeners = new OpenfeedListeners();
        listeners.OnConnected = connection => {
            Console.WriteLine("Connected.");
            return default;
        };
        listeners.OnDisconnected = () => {
            Console.WriteLine("Disconnected.");
            return default;
        };
        listeners.OnMessage = msg => {
            Console.WriteLine(msg.ToString());
            return default;
        };
        listeners.OnConnectFailed = ex => {
            Console.WriteLine(ex);
            return default;
        };
        listeners.OnCredentialsRejected = () => {
            Console.WriteLine("Credentials rejected.");
            return default;
        };

        // Prompt the user for the username and password

        Console.WriteLine("Username:");
        var username = Console.ReadLine();
        Console.WriteLine("Password:");
        var password = Console.ReadLine();

        // Create a client and subscribe to all MSFT messages

        var client = OpenfeedFactory.CreateClient(new Uri("ws://openfeed.aws.barchart.com/ws"), username, password, listeners);
        var subId = client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: new[] { "MSFT" });
        
        // Sleep forever on this thread while the client we just created calls the listener callbacks

        Thread.Sleep(Timeout.Infinite);
    }
}
```

This program is a simple amalgamation of what we learned before. We first set up a listener and connect the callbacks that simply print everything that's happening to the console. Next, we prompt the user for their credentials, and then finally we create a client, subscibe to all messages for the symbol MSFT, and then pause the thread while the client continues to run on the thread pool.

## More Information

To learn more about subscribing to symbols, exchanges and channels, click [here](SYMBOLS_EXCHANGES_CHANNELS.md).

To learn more about various subscription types, click [here](SUBSCRIPTION_TYPES.md).

