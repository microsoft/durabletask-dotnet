# Dapr Durable Task .NET Client SDK

[![License: Apache 2.0][apache-badge]][apache-url] [![NuGet Version](https://img.shields.io/nuget/v/Dapr.DurableTask?logo=nuget&label=Latest%20version&style=flat)](https://www.nuget.org/packages/Dapr.DurableTask)

[apache-badge]: https://img.shields.io/github/license/dapr/dapr?style=flat&label=License&logo=github
[apache-url]: https://github.com/dapr/dapr/blob/master/LICENSE
[discord-badge]: https://img.shields.io/discord/778680217417809931?label=Discord&style=flat&logo=discord
[discord-url]: http://bit.ly/dapr-discord

> This is  Dapr-specific fork of [Microsoft's .NET Durable Task Framework](https://github.com/microsoft/durabletask-dotnet).

To use this within your Dapr project, use the [Dapr.Workflow](https://www.nuget.org/packages/Dapr.Workflow) NuGet package. Refer to the [Dapr documentation](https://docs.dapr.io/developing-applications/sdks/dotnet/dotnet-workflow/dotnet-workflow-howto/) for more information.

If you would like to file any issues with this repository, please open them in the [Dapr .NET SDK repository](htts://github.com/dapr/dotnet-sdk).

## Obtaining the Protobuf definitions

This project utilizes protobuf definitions from [durabletask-protobuf](https://github.com/microsoft/durabletask-protobuf), which are copied (vendored) into this repository under the `src/Grpc` directory. See the corresponding [README.md](./src/Grpc/README.md) for more information about how to update the protobuf definitions.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
