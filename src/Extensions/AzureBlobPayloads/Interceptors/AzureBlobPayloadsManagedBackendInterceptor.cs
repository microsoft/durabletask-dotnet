// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

// using P = Microsoft.DurableTask.AzureManagedBackend.Protobuf;

// namespace Microsoft.DurableTask;

// /// <summary>
// /// gRPC interceptor that externalizes large payloads to an <see cref="PayloadStore"/> on requests
// /// and resolves known payload tokens on responses for Azure Managed Backend.
// /// </summary>
// public sealed class AzureBlobPayloadsManagedBackendInterceptor(PayloadStore payloadStore, LargePayloadStorageOptions options)
//     : BasePayloadInterceptor<object, object>(payloadStore, options)
// {
//     /// <inheritdoc/>
//     protected override async Task ExternalizeRequestPayloadsAsync<TRequest>(TRequest request, CancellationToken cancellation)
//     {
//         // Worker -> Backend
//         // print request type and namespace
//         Console.WriteLine($"--------------------------Request Namespace: {request.GetType().Namespace}");
//         Console.WriteLine($"--------------------------Request Name: {request.GetType().Name}");
//         Console.WriteLine($"--------------------------Request Full Name: {request.GetType().FullName}");
//         Console.WriteLine($"--------------------------Completeorchestrat req full name: {typeof(P.CompleteOrchestrationWorkItemRequest).FullName}");
//         Console.WriteLine($"Assembly of request: {request.GetType().Assembly?.FullName}");
//         Console.WriteLine($"Same? {typeof(P.CompleteOrchestrationWorkItemRequest).Assembly}");

//         // Standard orchestration/entity instance operations
//         if (IsType(request, typeof(P.CreateInstanceRequest)))
//         {
//             dynamic r = request;
//             r.Input = await this.MaybeExternalizeAsync(r.Input, cancellation);
//         }
//         else if (IsType(request, typeof(P.RaiseEventRequest)))
//         {
//             dynamic r = request;
//             r.Input = await this.MaybeExternalizeAsync(r.Input, cancellation);
//         }
//         else if (IsType(request, typeof(P.TerminateRequest)))
//         {
//             dynamic r = request;
//             r.Output = await this.MaybeExternalizeAsync(r.Output, cancellation);
//         }
//         else if (IsType(request, typeof(P.SuspendRequest)))
//         {
//             dynamic r = request;
//             r.Reason = await this.MaybeExternalizeAsync(r.Reason, cancellation);
//         }
//         else if (IsType(request, typeof(P.ResumeRequest)))
//         {
//             dynamic r = request;
//             r.Reason = await this.MaybeExternalizeAsync(r.Reason, cancellation);
//         }
//         else if (IsType(request, typeof(P.SignalEntityRequest)))
//         {
//             dynamic r = request;
//             r.Input = await this.MaybeExternalizeAsync(r.Input, cancellation);
//         }

//         // Backend-specific work item operations
//         else if (IsType(request, typeof(P.AddEventRequest)))
//         {
//             dynamic r = request;
//             if (r.Event != null)
//             {
//                 await this.ExternalizeHistoryEventAsync(r.Event, cancellation);
//             }
//         }
//         else if (IsType(request, typeof(P.CompleteActivityWorkItemRequest)))
//         {
//             dynamic r = request;
//             if (r.ResponseEvent != null)
//             {
//                 await this.ExternalizeHistoryEventAsync(r.ResponseEvent, cancellation);
//             }
//         }
//         else if (IsType(request, typeof(P.CompleteOrchestrationWorkItemRequest)))
//         {
//             dynamic r = request;
//             await this.ExternalizeCompleteOrchestrationWorkItemRequestAsync(r, cancellation);
//         }
//         else if (IsType(request, typeof(P.CompleteEntityWorkItemRequest)))
//         {
//             dynamic r = request;
//             await this.ExternalizeCompleteEntityWorkItemRequestAsync(r, cancellation);
//         }
//         else
//         {
//             // print request not hitting any of cases
//             Console.WriteLine($"--------------------------Request not hitting any of cases: {request.GetType().Name}");
//         }
//     }

//     /// <inheritdoc/>
//     protected override async Task ResolveResponsePayloadsAsync<TResponse>(TResponse response, CancellationToken cancellation)
//     {
//         // Backend -> Worker
//         if (IsType(response, typeof(P.GetInstanceResponse)))
//         {
//             P.GetInstanceResponse r = (P.GetInstanceResponse)(object)response;
//             if (r.OrchestrationState is { } s)
//             {
//                 await this.MaybeResolveAsync(v => s.Input = v, s.Input, cancellation);
//                 await this.MaybeResolveAsync(v => s.Output = v, s.Output, cancellation);
//                 await this.MaybeResolveAsync(v => s.CustomStatus = v, s.CustomStatus, cancellation);
//             }
//         }
//         else if (IsType(response, typeof(P.WaitForInstanceResponse)))
//         {
//             P.WaitForInstanceResponse r = (P.WaitForInstanceResponse)(object)response;
//             if (r.OrchestrationState is { } s)
//             {
//                 await this.MaybeResolveAsync(v => s.Input = v, s.Input, cancellation);
//                 await this.MaybeResolveAsync(v => s.Output = v, s.Output, cancellation);
//                 await this.MaybeResolveAsync(v => s.CustomStatus = v, s.CustomStatus, cancellation);
//             }
//         }
//         else if (IsType(response, typeof(P.QueryInstancesResponse)))
//         {
//             P.QueryInstancesResponse r = (P.QueryInstancesResponse)(object)response;
//             foreach (P.OrchestrationState s in r.OrchestrationState)
//             {
//                 await this.MaybeResolveAsync(v => s.Input = v, s.Input, cancellation);
//                 await this.MaybeResolveAsync(v => s.Output = v, s.Output, cancellation);
//                 await this.MaybeResolveAsync(v => s.CustomStatus = v, s.CustomStatus, cancellation);
//             }
//         }
//         else if (IsType(response, typeof(P.GetEntityResponse)))
//         {
//             P.GetEntityResponse r = (P.GetEntityResponse)(object)response;
//             if (r.Entity is { } em)
//             {
//                 await this.MaybeResolveAsync(v => em.SerializedState = v, em.SerializedState, cancellation);
//             }
//         }
//         else if (IsType(response, typeof(P.QueryEntitiesResponse)))
//         {
//             P.QueryEntitiesResponse r = (P.QueryEntitiesResponse)(object)response;
//             foreach (P.EntityMetadata em in r.Entities)
//             {
//                 await this.MaybeResolveAsync(v => em.SerializedState = v, em.SerializedState, cancellation);
//             }
//         }
//         else if (IsType(response, typeof(P.GetOrchestrationRuntimeStateResponse)))
//         {
//             P.GetOrchestrationRuntimeStateResponse r = (P.GetOrchestrationRuntimeStateResponse)(object)response;
//             if (r.History != null)
//             {
//                 foreach (P.HistoryEvent e in r.History)
//                 {
//                     await this.ResolveHistoryEventAsync(e, cancellation);
//                 }
//             }
//         }
//         else if (IsType(response, typeof(P.HistoryChunk)))
//         {
//             P.HistoryChunk c = (P.HistoryChunk)(object)response;
//             if (c.Events != null)
//             {
//                 foreach (P.HistoryEvent e in c.Events)
//                 {
//                     await this.ResolveHistoryEventAsync(e, cancellation);
//                 }
//             }
//         }
//         else if (IsType(response, typeof(P.WorkItem)))
//         {
//             P.WorkItem wi = (P.WorkItem)(object)response;
//             await this.ResolveWorkItemAsync(wi, cancellation);
//         }
//     }

//     async Task ExternalizeCompleteOrchestrationWorkItemRequestAsync(
//         P.CompleteOrchestrationWorkItemRequest request,
//         CancellationToken cancellation)
//     {
//         Console.WriteLine($"--------------------------CompleteOrchestrationWorkItemRequest Namespace: {request.GetType().Namespace}");

//         // Externalize custom status
//         if (!string.IsNullOrEmpty(request.CustomStatus))
//         {
//             request.CustomStatus = await this.MaybeExternalizeAsync(request.CustomStatus, cancellation);
//         }

//         // Externalize history events
//         if (request.NewHistory != null)
//         {
//             foreach (P.HistoryEvent e in request.NewHistory)
//             {
//                 await this.ExternalizeHistoryEventAsync(e, cancellation);
//             }
//         }

//         if (request.NewTasks != null)
//         {
//             foreach (P.HistoryEvent e in request.NewTasks)
//             {
//                 await this.ExternalizeHistoryEventAsync(e, cancellation);
//             }
//         }

//         if (request.NewTimers != null)
//         {
//             foreach (P.HistoryEvent e in request.NewTimers)
//             {
//                 await this.ExternalizeHistoryEventAsync(e, cancellation);
//             }
//         }

//         // Externalize messages (each contains a HistoryEvent)
//         if (request.NewMessages != null)
//         {
//             foreach (P.OrchestratorMessage msg in request.NewMessages)
//             {
//                 if (msg.Event != null)
//                 {
//                     await this.ExternalizeHistoryEventAsync(msg.Event, cancellation);
//                 }
//             }
//         }
//     }

//     async Task ExternalizeCompleteEntityWorkItemRequestAsync(
//         P.CompleteEntityWorkItemRequest request,
//         CancellationToken cancellation)
//     {
//         // Externalize entity state
//         if (!string.IsNullOrEmpty(request.EntityState))
//         {
//             request.EntityState = await this.MaybeExternalizeAsync(request.EntityState, cancellation);
//         }

//         // Externalize messages (each contains a HistoryEvent)
//         if (request.Messages != null)
//         {
//             foreach (P.OrchestratorMessage msg in request.Messages)
//             {
//                 if (msg.Event != null)
//                 {
//                     await this.ExternalizeHistoryEventAsync(msg.Event, cancellation);
//                 }
//             }
//         }
//     }

//     async Task ExternalizeHistoryEventAsync(P.HistoryEvent e, CancellationToken cancellation)
//     {
//         Console.WriteLine($"--------------------------HistoryEvent Namespace: {e.GetType().Namespace}");
//         switch (e.EventTypeCase)
//         {
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted when e.ExecutionStarted is { } es:
//                 es.Input = await this.MaybeExternalizeAsync(es.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted when e.ExecutionCompleted is { } ec:
//                 ec.Result = await this.MaybeExternalizeAsync(ec.Result, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated when e.ExecutionTerminated is { } ef:
//                 ef.Input = await this.MaybeExternalizeAsync(ef.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EventRaised when e.EventRaised is { } er:
//                 er.Input = await this.MaybeExternalizeAsync(er.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.TaskScheduled when e.TaskScheduled is { } ts:
//                 ts.Input = await this.MaybeExternalizeAsync(ts.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.TaskCompleted when e.TaskCompleted is { } tc:
//                 tc.Result = await this.MaybeExternalizeAsync(tc.Result, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.TaskFailed when e.TaskFailed is { } tf && tf.FailureDetails is { } fd:
//                 fd.StackTrace = await this.MaybeExternalizeAsync(fd.StackTrace, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated when e.SubOrchestrationInstanceCreated is { } soc:
//                 soc.Input = await this.MaybeExternalizeAsync(soc.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted when e.SubOrchestrationInstanceCompleted is { } sox:
//                 sox.Result = await this.MaybeExternalizeAsync(sox.Result, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed when e.SubOrchestrationInstanceFailed is { } sof && sof.FailureDetails is { } fd:
//                 fd.StackTrace = await this.MaybeExternalizeAsync(fd.StackTrace, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EventSent when e.EventSent is { } esent:
//                 esent.Input = await this.MaybeExternalizeAsync(esent.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.GenericEvent when e.GenericEvent is { } ge:
//                 ge.Data = await this.MaybeExternalizeAsync(ge.Data, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew when e.ContinueAsNew is { } can:
//                 can.Input = await this.MaybeExternalizeAsync(can.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated when e.ExecutionTerminated is { } et:
//                 et.Input = await this.MaybeExternalizeAsync(et.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended when e.ExecutionSuspended is { } esus:
//                 esus.Input = await this.MaybeExternalizeAsync(esus.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionResumed when e.ExecutionResumed is { } eres:
//                 eres.Input = await this.MaybeExternalizeAsync(eres.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled when e.EntityOperationSignaled is { } eos:
//                 eos.Input = await this.MaybeExternalizeAsync(eos.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled when e.EntityOperationCalled is { } eoc:
//                 eoc.Input = await this.MaybeExternalizeAsync(eoc.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted when e.EntityOperationCompleted is { } ecomp:
//                 ecomp.Output = await this.MaybeExternalizeAsync(ecomp.Output, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationFailed when e.EntityOperationFailed is { } eof && eof.FailureDetails is { } fd:
//                 fd.StackTrace = await this.MaybeExternalizeAsync(fd.StackTrace, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.HistoryState when e.HistoryState is { } hs && hs.OrchestrationState is { } os:
//                 os.Input = await this.MaybeExternalizeAsync(os.Input, cancellation);
//                 os.Output = await this.MaybeExternalizeAsync(os.Output, cancellation);
//                 os.CustomStatus = await this.MaybeExternalizeAsync(os.CustomStatus, cancellation);
//                 break;
//         }
//     }

//     async Task ResolveWorkItemAsync(P.WorkItem wi, CancellationToken cancellation)
//     {
//         // Resolve activity input
//         if (wi.ActivityRequest is { } ar)
//         {
//             await this.MaybeResolveAsync(v => ar.Input = v, ar.Input, cancellation);
//         }

//         // Resolve orchestration input embedded in ExecutionStarted event and external events
//         if (wi.OrchestratorRequest is { } or)
//         {
//             if (or.PastEvents != null)
//             {
//                 foreach (P.HistoryEvent e in or.PastEvents)
//                 {
//                     await this.ResolveHistoryEventAsync(e, cancellation);
//                 }
//             }

//             if (or.NewEvents != null)
//             {
//                 foreach (P.HistoryEvent e in or.NewEvents)
//                 {
//                     await this.ResolveHistoryEventAsync(e, cancellation);
//                 }
//             }
//         }

//         // Resolve entity V2 request (history-based operation requests and entity state)
//         if (wi.EntityRequestV2 is { } er2)
//         {
//             await this.MaybeResolveAsync(v => er2.EntityState = v, er2.EntityState, cancellation);

//             if (er2.OperationRequests != null)
//             {
//                 foreach (P.HistoryEvent opEvt in er2.OperationRequests)
//                 {
//                     await this.ResolveHistoryEventAsync(opEvt, cancellation);
//                 }
//             }
//         }
//     }

//     async Task ResolveHistoryEventAsync(P.HistoryEvent e, CancellationToken cancellation)
//     {
//         switch (e.EventTypeCase)
//         {
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionStarted when e.ExecutionStarted is { } es:
//                 await this.MaybeResolveAsync(v => es.Input = v, es.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionCompleted when e.ExecutionCompleted is { } ec:
//                 await this.MaybeResolveAsync(v => ec.Result = v, ec.Result, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated when e.ExecutionTerminated is { } ef:
//                 await this.MaybeResolveAsync(v => ef.Input = v, ef.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EventRaised when e.EventRaised is { } er:
//                 await this.MaybeResolveAsync(v => er.Input = v, er.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.TaskScheduled when e.TaskScheduled is { } ts:
//                 await this.MaybeResolveAsync(v => ts.Input = v, ts.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.TaskCompleted when e.TaskCompleted is { } tc:
//                 await this.MaybeResolveAsync(v => tc.Result = v, tc.Result, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.TaskFailed when e.TaskFailed is { } tf && tf.FailureDetails is { } fd:
//                 await this.MaybeResolveAsync(v => fd.StackTrace = v, fd.StackTrace, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCreated when e.SubOrchestrationInstanceCreated is { } soc:
//                 await this.MaybeResolveAsync(v => soc.Input = v, soc.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceCompleted when e.SubOrchestrationInstanceCompleted is { } sox:
//                 await this.MaybeResolveAsync(v => sox.Result = v, sox.Result, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.SubOrchestrationInstanceFailed when e.SubOrchestrationInstanceFailed is { } sof && sof.FailureDetails is { } fd:
//                 await this.MaybeResolveAsync(v => fd.StackTrace = v, fd.StackTrace, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EventSent when e.EventSent is { } esent:
//                 await this.MaybeResolveAsync(v => esent.Input = v, esent.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.GenericEvent when e.GenericEvent is { } ge:
//                 await this.MaybeResolveAsync(v => ge.Data = v, ge.Data, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ContinueAsNew when e.ContinueAsNew is { } can:
//                 await this.MaybeResolveAsync(v => can.Input = v, can.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionTerminated when e.ExecutionTerminated is { } et:
//                 await this.MaybeResolveAsync(v => et.Input = v, et.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionSuspended when e.ExecutionSuspended is { } esus:
//                 await this.MaybeResolveAsync(v => esus.Input = v, esus.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.ExecutionResumed when e.ExecutionResumed is { } eres:
//                 await this.MaybeResolveAsync(v => eres.Input = v, eres.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationSignaled when e.EntityOperationSignaled is { } eos:
//                 await this.MaybeResolveAsync(v => eos.Input = v, eos.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationCalled when e.EntityOperationCalled is { } eoc:
//                 await this.MaybeResolveAsync(v => eoc.Input = v, eoc.Input, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationCompleted when e.EntityOperationCompleted is { } ecomp:
//                 await this.MaybeResolveAsync(v => ecomp.Output = v, ecomp.Output, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.EntityOperationFailed when e.EntityOperationFailed is { } eof && eof.FailureDetails is { } fd:
//                 await this.MaybeResolveAsync(v => fd.StackTrace = v, fd.StackTrace, cancellation);
//                 break;
//             case P.HistoryEvent.EventTypeOneofCase.HistoryState when e.HistoryState is { } hs && hs.OrchestrationState is { } os:
//                 await this.MaybeResolveAsync(v => os.Input = v, os.Input, cancellation);
//                 await this.MaybeResolveAsync(v => os.Output = v, os.Output, cancellation);
//                 await this.MaybeResolveAsync(v => os.CustomStatus = v, os.CustomStatus, cancellation);
//                 break;
//         }
//     }
// }
