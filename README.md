# .NET SDK for Barchart OpenFeed

The .NET SDK for Barchart OpenFeed is a library that can be used to subscribe to market data messages served by the Barchart [OpenFeed](https://openfeed.com/) servers. The documentation about the OpenFeed protocol is available [here](https://openfeed-org.github.io/documentation/).

## Obtaining the Library

The easiest way to get started is to add the openfeed.net package from [NuGet](https://www.nuget.org/packages/openfeed.net/). The latest version is 1.0.3.

## This Repository

This repository contains a solution with three projects:

1. Org.Openfeed.Client is the source code of the openfeed.net library.
2. Org.Openfeed.Client.Demo is the demo project which demonstrates the use of the above library.
3. Org.Openfeed.Messages is the source code of the project containing the Openfeed message definitions. It has been auto-generated from the Openfeed protocol buffer message definitions.

A good way to learn about using openfeed.net is to clone the repository and poke around the demo source code.

## User Guide

The User Guide for this project can be found in the [documentation](DOCUMENTATION.md) page.