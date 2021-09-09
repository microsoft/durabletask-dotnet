//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;

namespace DurableTask;

/// <summary>
/// Enum describing the status of the orchestration
/// </summary>
public enum OrchestrationRuntimeStatus
{
    /// <summary>
    /// The orchestration started running.
    /// </summary>
    Running,

    /// <summary>
    /// The orchestration completed normally.
    /// </summary>
    Completed,

    /// <summary>
    /// The orchestration is transitioning into a new instance.
    /// </summary>
    [Obsolete("The ContinuedAsNew status is obsolete and exists only for compatibility reasons.")]
    ContinuedAsNew,

    /// <summary>
    /// The orchestration completed with an unhandled exception.
    /// </summary>
    Failed,

    /// <summary>
    /// The orchestration canceled gracefully.
    /// </summary>
    [Obsolete("The Canceled status is not currently used and exists only for compatibility reasons.")]
    Canceled,

    /// <summary>
    /// The orchestration was abuptly terminated via a management API call.
    /// </summary>
    Terminated,

    /// <summary>
    /// The orchestration was scheduled but hasn't started running.
    /// </summary>
    Pending,
}
