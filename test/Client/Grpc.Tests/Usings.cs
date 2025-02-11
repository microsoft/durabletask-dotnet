// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER
global using Grpc.Net.Client;
#endif

#if NETFRAMEWORK
global using Grpc.Core;
global using GrpcChannel = Grpc.Core.Channel;
#endif
