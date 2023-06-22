// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

global using Grpc.Core;

#if NET6_0_OR_GREATER
global using Grpc.Net.Client;
#endif

#if NETSTANDARD2_0
global using GrpcChannel = Grpc.Core.Channel;
#endif
