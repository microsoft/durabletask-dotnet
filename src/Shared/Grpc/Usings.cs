// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER
global using Grpc.Net.Client;
#endif

#if NETSTANDARD2_0
global using Grpc.Core;
global using GrpcChannel = Grpc.Core.Channel;
#endif
