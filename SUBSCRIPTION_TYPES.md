# Subscription Types

Here are a few example various subscription types:

## Trades Subscription

You can use the SubscriptionType.Trades enumeration value to subscribe to trades and top of book messages:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.Trades, 1, symbols: new[] { "MSFT" });
```

This would result in a subscriptionResponse message, followed by an instrumentDefinition message, followed by a marketSnapshot (more or less a quote), followed by marketUpdate messages containing the top of the book and/or trades:

```json
{
    "subscriptionResponse": {
        "correlationId": "2",
        "status": {
            "result": "SUCCESS",
            "service": "REAL_TIME"
        },
        "symbol": "MSFT",
        "marketId": "8262209587480783120",
        "channelId": 16
    }
}
...
{
    "instrumentDefinition": {
        "marketId": "8262209587480783120",
        "instrumentType": "EQUITY",
        "vendorId": "NASDAQ_UTP",
        "symbol": "MSFT",
        "exchangeCode": "XNAS",
        "recordCreateTime": "1616017119766000000",
        "recordUpdateTime": "1616017119766000000",
        "timeZoneName": "America/New_York",
        "channel": 16,
        "priceFormat": {
            "denominator": 100,
            "subDenominator": 1
        },
        "priceDenominator": 10000,
        "quantityDenominator": 1,
        "symbols": [
            {
                "vendor": "Barchart",
                "symbol": "MSFT"
            }
        ],
        "consolidatedFeedInstrument": true
    }
}

...

{
    "marketSnapshot": {
        ...
    }
}

...

{
	"marketUpdate": {
		"marketId": "8262209587480783120",
		"symbol": "MSFT",
		"transactionTime": "1616059694054478848",
		"distributionTime": "1616059694054858551",
		"marketSequence": "3882",
		"originatorId": "UA==",
		"bbo": {
			"bidPrice": "2345000",
			"bidQuantity": "11",
			"bidOriginator": "UQ==",
			"offerPrice": "2356000",
			"offerQuantity": "1",
			"offerOriginator": "UA==",
			"quoteCondition": "Ug=="
		}
	}
}

...

{
	"marketUpdate": {
		"marketId": "8262209587480783120",
		"symbol": "MSFT",
		"transactionTime": "1616059700690841000",
		"marketSequence": "3883",
		"session": {
			"volume": {
				"volume": "18330"
			},
			"numberOfTrades": {
				"numberTrades": "374"
			},
			"monetaryValue": {
				"value": "430158642"
			}
		},
		"trades": {
			"trades": [
				{
					"trade": {
						"originatorId": "UQ==",
						"transactionTime": "1616059700671550189",
						"price": "2345900",
						"quantity": "1",
						"tradeId": "MjA4",
						"tradeDate": 20210318,
						"saleCondition": "QEZUSQ==",
						"doesNotUpdateLast": true,
						"session": "I",
						"distributionTime": "1616059700671565461"
					}
				}
			]
		}
	}
}

...
```

## OHLC Subscription

The Ohlc susbscription type will give you periodic Open + High + Low + Close messages:

```C#
client.Subscribe(Service.RealTime, SubscriptionType.Ohlc, 1, symbols: new[] { "MSFT" });
```

Just like the trades subscription above, this would result in a subscriptionResponse message, followed by an instrumentDefinition message, followed by by periodic OHLC messages:

```json
...

{
	"ohlc": {
		"marketId": "8262209587480783120",
		"symbol": "MSFT",
		"open": {
			"price": "2350000"
		},
		"high": {
			"price": "2350000"
		},
		"low": {
			"price": "2350000"
		},
		"close": {
			"price": "2350000"
		}
	}
}

...
```

## Quote Subscrition

```C#
client.Subscribe(Service.RealTime, SubscriptionType.Quote, 1, symbols: new[] { "MSFT" });
```

Again, we first received the subscription response and the instrumentDefinition messages, followed by marketSnapshot and marketUpdate messages:

```json
...

{
    "marketSnapshot": {
        ...
    }
}
    
...

{ 
    "marketUpdate": {
        ...
    }
}

...
```

## All Subscription Types

```C#
client.Subscribe(Service.RealTime, SubscriptionType.All, 1, symbols: new[] { "MSFT" });
```

The All subscription type will give you all the messages (quotes, trades, book, depth, etc.) that you are permissioned for.