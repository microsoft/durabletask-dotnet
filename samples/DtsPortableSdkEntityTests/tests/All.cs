// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DtsPortableSdkEntityTests;

/// <summary>
/// A collection containing all the unit tests.
/// </summary>
static class All
{
    public static IEnumerable<Test> GetAllTests()
    {
        yield return new SetAndGet();
        yield return new CallCounter();
        yield return new BatchedEntitySignals(100);
        yield return new SignalAndCall(typeof(StringStore));
        yield return new SignalAndCall(typeof(StringStore2));
        yield return new SignalAndCall(typeof(StringStore3));
        yield return new CallAndDelete(typeof(StringStore));
        yield return new CallAndDelete(typeof(StringStore2));
        yield return new CallAndDelete(typeof(StringStore3));
        yield return new DeleteAfterLock();
        yield return new SignalThenPoll(direct: true, delayed: false);
        yield return new SignalThenPoll(direct: true, delayed: true);
        yield return new SignalThenPoll(direct: false, delayed: false);
        yield return new SignalThenPoll(direct: false, delayed: true);
        yield return new SelfScheduling();
        yield return new FireAndForget(null);
        yield return new FireAndForget(0);
        yield return new FireAndForget(5);
        yield return new SingleLockedTransfer();
        yield return new MultipleLockedTransfers(2);
        yield return new MultipleLockedTransfers(5);
        yield return new MultipleLockedTransfers(100);
        yield return new TwoCriticalSections(sameEntity: true);
        yield return new TwoCriticalSections(sameEntity: false);
        yield return new FaultyCriticalSection();
        yield return new LargeEntity();
        yield return new CallSingleFaultyEntity();
        yield return new CallMultipleFaultyEntities();
        yield return new CallFaultyActivity(nested: false);
        yield return new CallFaultyActivity(nested: true);
        yield return new CallFaultySuborchestration(nested: false);
        yield return new CallFaultySuborchestration(nested: true);
        yield return new InvalidEntityId(InvalidEntityId.Location.ClientGet);
        yield return new InvalidEntityId(InvalidEntityId.Location.ClientSignal);
        yield return new InvalidEntityId(InvalidEntityId.Location.OrchestrationCall);
        yield return new InvalidEntityId(InvalidEntityId.Location.OrchestrationSignal);
        yield return new EntityQueries1();
        yield return new EntityQueries2();
        yield return new NoOrphanedLockAfterTermination();
        yield return new NoOrphanedLockAfterNondeterminism();
    }

}
